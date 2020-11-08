using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;
using TSLab.Script.Options;
using TSLab.Script.Realtime;

namespace TwoLegArbitrage
{
    public class TwoLegArbitrage : IExternalScript2
    {
        #region === Параметры робота ===
        // параметры индикаторов
        public IntOptimProperty PeriodMa = new IntOptimProperty(100, 10, 300, 10);
        public OptimProperty Multiply = new OptimProperty(1.0, 0.1, 100.0, 0.1);
        public OptimProperty UpChannel = new OptimProperty(1.0, 1.0, 2.5, 0.1);
        public OptimProperty DownChannel = new OptimProperty(1.0, 1.0, 2.5, 0.1);


        // объем входа в позицию в процентах
        public IntOptimProperty VolumePct = new IntOptimProperty(50, 10, 300, 10);

        // режим входа в позицию фиксированным объемом
        public BoolOptimProperty OnVolumeFixed = new BoolOptimProperty(true);
        public OptimProperty VolumeFixed1 = new OptimProperty(1.0, 1.0, 100.0, 1.0);

        // проскальзывание
        public IntOptimProperty Slippage = new IntOptimProperty(100, 0, 500, 10);

        // количество знаков после запятой для объема
        public IntOptimProperty VolumeDecimals = new IntOptimProperty(4, 1, 10, 1);

        // размер комиссии
        public OptimProperty CommissionPct = new OptimProperty(0.1, 0, 0.2, 0.01);

        #endregion

        #region=== Внутренние переменные робота ===
        // контекст в TSLab
        private IContext _ctx;

        // инструмент в TSLab
        private ISecurity _sec1;
        private ISecurity _sec2;

        // размер тика
        private double _tick1;
        private double _tick2;

        // стартовый бар
        private int _startBar;

        // количество бар для расчета
        private int _barsCount;

        // индикаторы робота
        private IList<double> _spred;
        private IList<double> _maSpred;
        private IList<double> _delta;
        private IList<double> _upChannel;
        private IList<double> _downChannel;

        // вывод сигналов на график
        double[] _arrSignalLE1;
        double[] _arrSignalSE1;
        double[] _arrSignalLE2;
        double[] _arrSignalSE2;

        #endregion

        public void Execute(IContext ctx, ISecurity sec1, ISecurity sec2)
        {
            // запуск таймера для определения времени выполнения скрипта
            var sw = Stopwatch.StartNew();

            _ctx = ctx;
            _sec1 = sec1;
            _sec2 = sec2;

            if (ctx.Runtime.IsAgentMode)
            {
                _tick1 = sec1.Tick;
                _tick2 = sec2.Tick;
            }
            else
            {
                _tick1 = 0.01;
                _tick2 = 0.01;
            }

            // расчет комиссии - комиссия относительная
            sec1.Commission = (pos, price, shares, isEntry, isPart) =>
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
            sec2.Commission = (pos, price, shares, isEntry, isPart) =>
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
            TradeCicle(sec1.ClosePrices, sec2.ClosePrices);

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
            _barsCount = Math.Min(_sec1.Bars.Count, _sec2.Bars.Count);

            if (!_ctx.IsLastBarUsed)
            {
                _barsCount--;
            }

            if(_barsCount <=0)
                throw new ArgumentException($"_barsCount = {_barsCount} <= 0");

            // первый используемый для расчетов бар
            _startBar = Math.Max(PeriodMa.Value + 1, _ctx.TradeFromBar);
        }

        /// <summary>
        /// Расчет индикаторов робота c кэшированием
        /// </summary>
        private void CalcIndicators()
        {
            // вычисляем спред межде инструментами - используем отношение
            _spred = _sec1.ClosePrices.Divide(_sec2.ClosePrices);
            
            // усредняем спред
            _maSpred = _ctx.GetData("maSpred", new string[] { PeriodMa.ToString() },
                () => Series.SMA(_spred, PeriodMa.Value));

            // вычисляем дельту - отклонение спреда от средней
            _delta = _spred.Subtract(_maSpred);

            // домножаем дельту на заданный коэффициент
            _delta = _delta.MultiplyConst(Multiply.Value);

            // задаем массивы верхней и нижней границы для дельты
            _upChannel = new double[_barsCount];
            _downChannel = new double[_barsCount];
            
            for (int i = 0; i < _barsCount; i++)
            {
                _upChannel[i] = UpChannel.Value;
                _downChannel[i] = DownChannel.Value;
            }

            // создаем массивы для дополнительного вывода торговых сигналов на графики
            _arrSignalLE1 = new double[_barsCount];
            _arrSignalSE1 = new double[_barsCount];
            _arrSignalLE2 = new double[_barsCount];
            _arrSignalSE2 = new double[_barsCount];

        }

        private void TradeCicle(IList<double> closePrices1, IList<double> closePrices2)
        {
            for (int i = _startBar; i < _barsCount; i++)
            {
                var lastPrice1 = closePrices1[i];
                var lastPrice2 = closePrices2[i];

                // проверка на корректность цены
                if (lastPrice1 <= 0 || lastPrice2 <= 0)
                {
                    _ctx.LogError("TradeCicle: некорректное значение цены инструмента");
                    return;
                }

                // получаем активные позиции
                var longPosition1 = _sec1.Positions.GetLastActiveForSignal("LE1", i);
                var shortPosition1 = _sec1.Positions.GetLastActiveForSignal("SE1", i);
                var longPosition2 = _sec2.Positions.GetLastActiveForSignal("LE2", i);
                var shortPosition2 = _sec2.Positions.GetLastActiveForSignal("SE2", i);
                 

                // торговые сигналы на вход в позицию, выход из позиции
                bool signalLE1, signalSE1, signalLX1, signalSX1;
                bool signalLE2, signalSE2, signalLX2, signalSX2;

                // сигналы при пробитии дельтой верхней границы канала
                signalSE1 = signalLE2 = _delta[i] > _upChannel[i] &&
                                        _delta[i - 1] <= _upChannel[i - 1] &&
                                        shortPosition1 == null &&
                                        longPosition1 == null &&
                                        shortPosition2 == null &&
                                        longPosition2 == null;
                            
                // противоположные сигналы на закрытие
                signalSX1 = signalLX2 = _delta[i] <= 0;

                // сигналы при пробитии дельтой нижней границы канала
                signalLE1 = signalSE2 = _delta[i] < _downChannel[i] &&
                                        _delta[i - 1] >= _downChannel[i - 1] &&
                                        shortPosition1 == null &&
                                        longPosition1 == null &&
                                        shortPosition2 == null &&
                                        longPosition2 == null;

                // противоположные сигналы на закрытие
                signalLX1 = signalSX2 = _delta[i] >= 0;

                _arrSignalLE1[i] = signalLE1 ? 1 : 0;
                _arrSignalSE1[i] = signalSE1 ? 1 : 0;
                _arrSignalLE2[i] = signalLE2 ? 1 : 0;
                _arrSignalSE2[i] = signalSE2 ? 1 : 0;


                double price1, price2;
                double volume1, volume2;


                // если нет позции, то проверяем условие на вход в позицию
                if (longPosition1 == null && shortPosition1 == null &&
                    longPosition2 == null && shortPosition2 == null)
                {
                    // пробой дельтой верхней границы канала
                    // sec1 - продаем, sec2 - покупаем
                    if (signalSE1)
                    {
                        price1 = lastPrice1 - Slippage.Value * _tick1;
                        price2 = lastPrice2 + Slippage.Value * _tick2;

                        if (OnVolumeFixed.Value)
                        {
                            volume1 = Math.Round(VolumeFixed1.Value, VolumeDecimals.Value);
                            volume2 = Math.Round(volume1 * price1 / price2, VolumeDecimals.Value);
                        }
                        else
                        {
                            volume1 = GetVolume(_sec1, i, lastPrice1);
                            volume2 = Math.Round(volume1 * price1 / price2, VolumeDecimals.Value);
                        }
                        _sec1.Positions.SellAtPrice(i + 1, volume1, price1, "SE1");
                        _sec2.Positions.BuyAtPrice(i + 1, volume2, price2, "LE2");
                    }
                    // пробой дельтой нижней границы канала
                    // sec1 - покупаем, sec2 - продаем
                    else if (signalLE1)
                    {
                        price1 = lastPrice1 + Slippage.Value * _tick1;
                        price2 = lastPrice2 - Slippage.Value * _tick2;

                        if (OnVolumeFixed.Value)
                        {
                            volume1 = Math.Round(VolumeFixed1.Value, VolumeDecimals.Value);
                            volume2 = Math.Round(volume1 * price1 / price2, VolumeDecimals.Value);
                        }
                        else
                        {
                            volume1 = GetVolume(_sec1, i, lastPrice1);
                            volume2 = Math.Round(volume1 * price1 / price2, VolumeDecimals.Value);
                        }
                        _sec1.Positions.BuyAtPrice(i + 1, volume1, price1, "LE1");
                        _sec2.Positions.SellAtPrice(i + 1, volume2, price2, "SE2");
                    }
                }
                // если есть позиции, то проверяем условия выхода из позиции
                else
                {
                    if (longPosition1 != null && signalLX1)
                    {
                        var price = lastPrice1 - Slippage.Value * _tick1;
                        longPosition1.CloseAtPrice(i + 1, price, "LX1");
                    }
                    
                    if (shortPosition1 != null && signalSX1)
                    {
                        var price = lastPrice1 + Slippage.Value * _tick1;
                        shortPosition1.CloseAtPrice(i + 1, price, "SX1");
                    }
                    
                    if (longPosition2 != null && signalLX2)
                    {
                        var price = lastPrice2 - Slippage.Value * _tick2;
                        longPosition2.CloseAtPrice(i + 1, price, "LX2");
                    }
                    
                    if (shortPosition2 != null && signalSX2)
                    {
                        var price = lastPrice2 + Slippage.Value * _tick2;
                        shortPosition2.CloseAtPrice(i + 1, price, "SX2");
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
            if (price <= 0)
            {
                _ctx.LogError($"GetVolume: некорректная цена {price}");
                return 0.0;
            }

            // получить размер депозита
            double depositSize = GetDepositSize(bar);

            // объем входа в позицию
            double volume = depositSize / price * VolumePct.Value / 100.0;

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
        double GetDepositSize(int bar)
        {
            if (bar < 0 || bar >= _barsCount)
            {
                return 0.0;
            }

            // если подключены к реальному счету, то получаем депозит со счета
            if (_sec1 is ISecurityRt secRt1)
            {
                return secRt1.CurrencyBalance;
            }

            // начальное значение депозита
            double deposit = _sec1.InitDeposit;
            
            // вычисляем текущее значение депозита,
            foreach (IPosition pos in _sec1.Positions.GetClosedOrActiveForBar(bar))
            {
                if (pos.IsActiveForBar(bar))
                {
                    deposit -= pos.EntryPrice * pos.Shares * _sec1.LotSize;
                }
                else
                {
                    deposit += pos.Profit();
                }
            }

            foreach (IPosition pos in _sec2.Positions.GetClosedOrActiveForBar(bar))
            {
                if (pos.IsActiveForBar(bar))
                {
                    deposit -= pos.EntryPrice * pos.Shares * _sec2.LotSize;
                }
                else
                {
                    deposit += pos.Profit();
                }
            }

            return deposit;
        }

        /// <summary>
        /// Вывод графиков в TSLab
        /// </summary>
        private void DrawGraph()
        {
            // Если идет процесс оптимизации, то графики не выводим
            if (_ctx.IsOptimization)
                return;

            IGraphPane pane1 = _ctx.First ?? _ctx.CreateGraphPane("First", "First");
            Color colorCandle = ScriptColors.Black;

            var graphSec1 = pane1.AddList(_sec1.Symbol,
                _sec1,
                CandleStyles.BAR_CANDLE,
                colorCandle,
                PaneSides.RIGHT);

            IGraphPane pane2 = _ctx.CreateGraphPane("Second", "Second");
            Color colorCandle2 = ScriptColors.Black;

            var graphSec2 = pane2.AddList(_sec2.Symbol,
                _sec2,
                CandleStyles.BAR_CANDLE,
                colorCandle,
                PaneSides.RIGHT);

            IGraphPane pane3 = _ctx.CreateGraphPane("Delta", "Delta");
            Color colorCandle3 = ScriptColors.Black;
            var graphSpred = pane3.AddList("Spred",
                _delta,
                ListStyles.HISTOHRAM,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var graphUpChannel = pane3.AddList($"UpChannel",
                _upChannel,
                ListStyles.LINE,
                ScriptColors.Red,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var graphDownChannel = pane3.AddList($"DownChannel",
                _downChannel,
                ListStyles.LINE,
                ScriptColors.Red,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            /*
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
            _arrVolume = new double[_barsCount];
            for (int i = 0; i < _barsCount; i++)
            {
                var activePositions1 = _sec1.Positions.GetActiveForBar(i).ToList();
                graphSec1.SetColor(i, activePositions1.Any() ? ScriptColors.Black : ScriptColors.DarkGray);
                var activePositions2 = _sec1.Positions.GetActiveForBar(i).ToList();
                graphSec2.SetColor(i, activePositions2.Any() ? ScriptColors.Black : ScriptColors.DarkGray);
            }
            /*
            IGraphPane pane4 = _ctx.CreateGraphPane("Four", "Four");
            var graphVolume = pane4.AddList("Volume",
                _arrVolume,
                ListStyles.HISTOHRAM_LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);
            */
        }
    }
}
