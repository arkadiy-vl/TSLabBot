using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;
using TSLab.Script.Realtime;

namespace BotBollingerTrend
{
    public class BotBollingerTrend : IExternalScript
    {
        #region === Параметры робота ===
        // параметры боинджера
        public IntOptimProperty PeriodBollinger = new IntOptimProperty(50, 10, 100, 5);
        public OptimProperty DeviationBollinger = new OptimProperty(1.5, 1, 2.5, 0.1);
        
        // объем входа в позицию в процентах
        public IntOptimProperty VolumePct = new IntOptimProperty(50, 10, 300, 10);

        // режим входа в позицию фиксированным объемом
        public BoolOptimProperty OnVolumeFixed = new BoolOptimProperty(true);
        public OptimProperty VolumeFixed = new OptimProperty(1.0, 1.0, 100.0, 1.0);

        // метод выхода из позиции: 1 - по противоположной границе канала, 2- по центру канала
        public IntOptimProperty MethodOutOfPosition = new IntOptimProperty(1, 1, 2, 1);
        
        // стопа для перевода позиции в безубыток
        public BoolOptimProperty OnStopForBreakeven = new BoolOptimProperty(false);
        public IntOptimProperty MinProfitOnStopBreakeven = new IntOptimProperty(5, 1, 10, 1);
        
        // проскальзывание
        public IntOptimProperty Slippage = new IntOptimProperty(100, 0, 500, 10);
        
        // количество знаков после запятой для объема
        public IntOptimProperty VolumeDecimals = new IntOptimProperty(0, 1, 10, 1);

        // размер комиссии
        public OptimProperty CommissionPct = new OptimProperty(0.1, 0, 0.2, 0.01);

        #endregion

        #region=== Внутренние переменные робота ===
        // контекст в TSLab
        private IContext _ctx;
        
        // инструмент в TSLab
        private ISecurity _sec;

        // размер тика
        private double _tick;

        // стартовый бар
        private int _startBar;

        // количество бар для расчета
        private int _barsCount;
        
        // индикаторы робота
        private IList<double> _upBollinger;
        private IList<double> _downBollinger;
        private IList<double> _centerBollinger;

        // вывод сигналов на график
        double[] _arrSignalLE;
        double[] _arrSignalSE;
        double[] _arrVolume;

        #endregion

        public void Execute(IContext ctx, ISecurity sec)
        {
            // запуск таймера для определения времени выполнения скрипта
            var sw = Stopwatch.StartNew();
            
            _ctx = ctx;
            _sec = sec;
            if (ctx.Runtime.IsAgentMode)
                _tick = sec.Tick;
            else
                _tick = sec.Tick;

            // расчет комиссии - комиссия относительная
            sec.Commission = (pos, price, shares, isEntry, isPart) =>
            {
                if (pos.Security.LotSize == 0)
                {
                    return (price * shares * CommissionPct.Value / 100.0);
                }
                else
                {
                    return (price * shares * pos.Security.LotSize * CommissionPct.Value / 100.0);
                }
            };

            // расчет начального и конечного бара TSLab
            CalcBarsTSL();

            
            

            // расчет индикаторов робота
            CalcIndicators();

            // торговый цикл
            TradeCicle(sec.ClosePrices);

            // вывод графиков
            DrawGraph();

            // пишем в лог только в режиме Лаборатория
            if (!ctx.Runtime.IsAgentMode) 
                ctx.LogInfo($"Скрипт выполнен за время: {sw.Elapsed}");
        }

        /// <summary>
        /// Расчет начального и конечного бара
        /// </summary>
        private void CalcBarsTSL()
        {
            // последний сформировавшийся бар для расчетов
            _barsCount = _sec.Bars.Count;
            if (!_ctx.IsLastBarUsed)
            {
                _barsCount--;
            }
            
            if(_barsCount <= 0)
                throw new ArgumentException($"_barsCount = {_barsCount} <= 0");

            // первый используемый для расчетов бар
            _startBar = Math.Max(PeriodBollinger.Value + 1, _ctx.TradeFromBar);

            // создаем массивы для дополнительного вывода торговых сигналов на графики
            _arrSignalLE = new double[_barsCount];
            _arrSignalSE = new double[_barsCount];
        }

        /// <summary>
        /// Расчет индикаторов робота c кэшированием
        /// </summary>
        private void CalcIndicators()
        {
            _upBollinger = _ctx.GetData("upBollinger", new string[] { PeriodBollinger.ToString(), DeviationBollinger.ToString() },
                () => Series.BollingerBands(_sec.ClosePrices, PeriodBollinger.Value, DeviationBollinger.Value, true));

            _downBollinger = _ctx.GetData("downBollinger", new string[] { PeriodBollinger.ToString(), DeviationBollinger.ToString() },
                () => Series.BollingerBands(_sec.ClosePrices, PeriodBollinger, DeviationBollinger, false));

            _centerBollinger = _ctx.GetData("centerBollinger",
                new string[] {PeriodBollinger.ToString(), DeviationBollinger.ToString()},
                () => TradeHelper.CenterChannel(_upBollinger, _downBollinger));
        }

        /// <summary>
        /// Основной торговый цикл 
        /// </summary>
        /// <param name="closePrices"></param>
        private void TradeCicle(IList<double> closePrices)
        {
            for (int i = _startBar; i < _barsCount; i++)
            {
                // проверка на корректность цены и болинджера
                if(closePrices[i] <= 0 || _upBollinger[i] <= 0 || _downBollinger[i] <= 0)
                {
                    _ctx.LogError("TradeCicle: некорректное значение цены или болинджера");
                    return;
                }

                // получаем активные позиции
                var longPosition = _sec.Positions.GetLastActiveForSignal("LE", i);
                var shortPosition = _sec.Positions.GetLastActiveForSignal("SE", i);

                // торговые сигналы на вход в позицию, выход из позиции
                bool signalLE, signalSE, signalLX, signalSX;

                signalLE = longPosition == null &&
                           closePrices[i] > _upBollinger[i] &&
                           closePrices[i - 1] <= _upBollinger[i - 1];

                signalSE = shortPosition == null &&
                           closePrices[i] < _downBollinger[i] &&
                           closePrices[i - 1] >= _downBollinger[i - 1];

                _arrSignalLE[i] = signalLE ? 1 : 0;
                _arrSignalSE[i] = signalSE ? 1 : 0;

                // сигналы на выход из позиции для варианта - выход по центру канала
                if(MethodOutOfPosition.Value == 2)
                {
                    signalLX = closePrices[i] < _centerBollinger[i];
                    signalSX = closePrices[i] > _centerBollinger[i];
                }
                // сигналы на выход из позиции для варианта - выход по противоположной границе канал
                else
                {
                    signalLX = closePrices[i] < _downBollinger[i];
                    signalSX = closePrices[i] > _upBollinger[i];
                }

                double volume;

                // если нет лонг позции, то проверяем условие на вход в лонг
                if (longPosition == null)
                {
                    if (signalLE)
                    {
                        if (OnVolumeFixed.Value)
                        {
                            volume = VolumeFixed.Value;
                        }
                        else
                        {
                            volume = GetVolume(_sec, i, closePrices[i]);
                        }
                        var price = closePrices[i] + Slippage.Value * _tick;
                        _sec.Positions.BuyAtPrice(i + 1, volume, price, "LE");
                        //_sec.Positions.BuyIfGreater(i+1, volume, price, "LE");
                    }
                }
                // если есть лонг позиция, то проверяем условия выхода из позиции
                else
                {
                    if (signalLX)
                    {
                        var price = closePrices[i] - Slippage.Value * _tick;
                        longPosition.CloseAtPrice(i + 1, price, "LX");
                    }
                    
                    // проверяем условия перевода позиции в безубыток
                    if (OnStopForBreakeven.Value)
                    {
                        // значение стоп-лоса на предыдущем баре
                        var prevStopPrice = longPosition.GetStop(i);

                        // если стоп-лосс уже был остановлен, то оставляем его
                        if (prevStopPrice != 0)
                        {
                            longPosition.CloseAtStop(i + 1, prevStopPrice, Slippage.Value * _tick,  "LXS");
                        }
                        else
                        {
                            // получаем профит по позиции
                            var profitPct = longPosition.OpenProfitPct(i);
                            if (profitPct > MinProfitOnStopBreakeven)
                            {
                                var stopPrice = longPosition.EntryPrice * (1 + 0.01);
                                longPosition.CloseAtStop(i + 1, stopPrice, Slippage.Value*_tick, "LXS");
                            }
                        }
                    }
                }

                // если нет шорт позиции, то проверяем условия на вход в шорт
                if (shortPosition == null)
                {
                    if (signalSE)
                    {
                        if (OnVolumeFixed.Value)
                        {
                            volume = VolumeFixed.Value;
                        }
                        else
                        {
                            volume = GetVolume(_sec, i, closePrices[i]);
                        }
                        var price = closePrices[i] - Slippage.Value * _tick;
                        _sec.Positions.SellAtPrice(i + 1, volume, price, "SE");
                        //_sec.Positions.SellIfLess(i + 1, volume, price, "SE");
                    }
                }
                // если есть шорт позияция, то проверяем условия выхода из позции
                else
                {
                    if (signalSX)
                    {
                        var price = closePrices[i] + Slippage.Value * _tick;
                        shortPosition.CloseAtPrice(i + 1, price, "SX");
                    }

                    // проверяем перевод позиции в безбыток
                    if (OnStopForBreakeven.Value)
                    {
                        // значение стоп-лоса на предыдущем баре
                        var prevStopPrice = shortPosition.GetStop(i);
                        
                        // если стоп-лосс уже был остановлен, то оставляем его
                        if (prevStopPrice != 0)
                        {
                            shortPosition.CloseAtStop(i + 1, prevStopPrice, Slippage.Value * _tick, "SXS");
                        }
                        else
                        {
                            // получаем профит по позиции
                            var profitPct = shortPosition.OpenProfitPct(i);
                            if (profitPct > MinProfitOnStopBreakeven)
                            {
                                var stopPrice = shortPosition.EntryPrice * (1 - 0.01);
                                shortPosition.CloseAtStop(i + 1, stopPrice, Slippage.Value * _tick, "SXS");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Получить объем входа в позицию по цене инструмента
        /// </summary>
        /// <param name="price"></param>
        /// <returns></returns>
        private double GetVolume(ISecurity sec, int bar, double price)
        {
            if (sec == null)
            {
                _ctx.LogError($"GetVolume: некорректный инструмент");
                return 0.0;
            }

            if (bar < 0 || bar > _barsCount)
            {
                _ctx.LogError($"GetVolume: некорректный номер бара {bar}");
                return 0.0;
            }

            // проверка на корректность переданной цены
            if(price <= 0)
            {
                _ctx.LogError($"GetVolume: некорректная цена {price}");
                return 0.0;
            }

            // получить размер депозита
            double depositSize = GetDepositSize(sec, bar);

            // объем входа в позицию
            double volume = depositSize / price * VolumePct.Value/100.0;

            // округление до заданного количества знаков после запятой
            volume = Math.Round(volume, VolumeDecimals.Value);

            return volume;
        }

        /// <summary>
        /// Получить размер депозита для заданного бара
        /// </summary>
        /// <param name="sec"></param>
        /// <param name="bar"></param>
        /// <returns></returns>
        double GetDepositSize(ISecurity sec, int bar)
        {
            if (sec == null)
                return 0.0;

            if (bar < 0 || bar >= _barsCount)
            {
                return 0.0;
            }

            // если подключены к реальному счету, то получаем депозит со счета
            if (sec is ISecurityRt secRt)
            {
                return secRt.CurrencyBalance;
            }

            // начальное значение депозита
            double deposit = sec.InitDeposit;

            // вычисляем текущее значение депозита,
            foreach (IPosition pos in sec.Positions.GetClosedOrActiveForBar(bar))
            {
                if (pos.IsActiveForBar(bar))
                {
                    deposit -= pos.EntryPrice * pos.Shares * sec.LotSize;
                }
                else
                {
                    deposit += pos.Profit();
                }
            }
            return deposit;
        }

        void GetPortfolioInfo(ISecurity sec)
        {
            if (sec is ISecurityRt secRt)
            {
                var provaderName = secRt.ProviderName;
                var portfolioName = secRt.PortfolioName;
                var currencyBalance = secRt.CurrencyBalance;
                var estimatedBalance = secRt.EstimatedBalance;
                var decimals = secRt.Decimals;
                var tick = secRt.Tick;

                _ctx.LogInfo("ProviderName: {0}, PortfolioName: {1}, CurrencyBalance: {2}, "+
                             " EstimatedBalance: {3}, Decimals: {4}, Tick: {5}", 
                    provaderName,
                    portfolioName,
                    currencyBalance,
                    estimatedBalance,
                    decimals,
                    tick);
            }
            else
            {
                _ctx.LogInfo("Режим лаборатории");
            }
        }

        /// <summary>
        /// Вывод графиков в TSLab
        /// </summary>
        private void DrawGraph()
        {
            // Если идет процесс оптимизации, то графики не выводим
            if (_ctx.IsOptimization)
                return;

            IGraphPane pane = _ctx.First ?? _ctx.CreateGraphPane("First", "First");
            Color colorCandle = ScriptColors.Black;

            var graphSec = pane.AddList(_sec.Symbol,
                _sec,
                CandleStyles.BAR_CANDLE,
                colorCandle,
                PaneSides.RIGHT);

            var graphUpBollinger = pane.AddList($"upBollinger ({PeriodBollinger} - {DeviationBollinger})",
                _upBollinger,
                ListStyles.LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var graphDownBollinger = pane.AddList($"downBollinger ({PeriodBollinger} - {DeviationBollinger})",
                _downBollinger,
                ListStyles.LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            if (MethodOutOfPosition.Value == 2)
            {
                var graphCenterBollinger = pane.AddList("centerBollinger",
                    _centerBollinger,
                    ListStyles.LINE,
                    ScriptColors.Blue,
                    LineStyles.SOLID,
                    PaneSides.RIGHT);
            }

            var graphSignalLE = pane.AddList("SignalLE",
                _arrSignalLE,
                ListStyles.HISTOHRAM,
                ScriptColors.Green,
                LineStyles.SOLID,
                PaneSides.LEFT);

            var graphignalSE = pane.AddList("SignalSE",
                _arrSignalSE,
                ListStyles.HISTOHRAM,
                ScriptColors.Red,
                LineStyles.SOLID,
                PaneSides.LEFT);

            // раскрашиваем бары в зависимости от наличия позиции
            // и определяем объем входа в позицию на каждом баре
            _arrVolume = new double[_barsCount];
            for (int i = 0; i < _barsCount; i++)
            {
                var activePositions = _sec.Positions.GetActiveForBar(i).ToList();
                graphSec.SetColor(i, activePositions.Any() ? ScriptColors.Black : ScriptColors.DarkGray);

                foreach(var pos in activePositions)
                {
                    if (pos.EntryBar.Date.Date == _sec.Bars[i].Date.Date &&
                        pos.EntryBar.Date.TimeOfDay == _sec.Bars[i].Date.TimeOfDay)
                        _arrVolume[i] += pos.Shares;
                }
            }

            IGraphPane pane2 = _ctx.CreateGraphPane("Second", "Second");
            var graphVolume = pane2.AddList("Volume",
                _arrVolume,
                ListStyles.HISTOHRAM_LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

        }
    }
}
