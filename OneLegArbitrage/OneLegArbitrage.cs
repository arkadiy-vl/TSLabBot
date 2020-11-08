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
using TSLab.Utils;

namespace OneLegArbitrage
{
    /// <summary>
    /// Робот для одноногого арбитража относительно индекса.
    /// В качестве торгуемого инструмента используется инструмент1 (sec1).
    /// Робот торгует на завершении свечи.
    /// Выбор вариантов построения канала индекса (спреда) ChannelIndicator: "MA_StDev", "Bollinger".
    /// Вход в позицию при выходе спреда за границы канала.
    /// Дополнительные входы в позицию при дальшейшем отклонении индекса на величину DeviationIndexForAddEnter (в %).
    /// Максимальное количество открываемых позиций в одном направлении задается настроечны  параметром MaxPositionsCount.
    /// Выбор объема входа: фиксированный объем FixedVolume, либо процент от депозита VolumePctOfDeposit (депозит в USDT).
    /// Выбор варианта выхода из позиций MethodOfExit: по центру канала (CenterChannel), по противоположной границе канала (BoundaryChannel).
    /// </summary>
    /// 
    public class OneLegArbitrage : IExternalScript4
    {
        #region === Параметры робота ===
        // параметры индикаторов
        public IntOptimProperty LenghtMa = new IntOptimProperty(100, 10, 300, 10);
        public IntOptimProperty LenghtStDev = new IntOptimProperty(20, 10, 100, 10);
        public OptimProperty MultiplyStDev = new OptimProperty(1.0, 0.1, 100.0, 0.1);
        public IntOptimProperty LenghtBollinger = new IntOptimProperty(100, 60, 300, 10);
        public OptimProperty DeviationBollinger = new OptimProperty(1, 0.5, 2.5, 0.5);

        // режим входа в позицию фиксированным объемом
        public BoolOptimProperty OnFixedVolume = new BoolOptimProperty(true);
        public OptimProperty FixedVolume = new OptimProperty(1.0, 0.1, 100.0, 0.1);

        // объем входа в позицию в процентах от депозита
        public IntOptimProperty VolumePctOfDeposit = new IntOptimProperty(30, 10, 100, 10);

        // проскальзывание в шагах цены
        public IntOptimProperty Slippage = new IntOptimProperty(200, 0, 500, 10);

        // количество знаков после запятой для объема
        public IntOptimProperty VolumeDecimals = new IntOptimProperty(4, 1, 10, 1);

        // индикатор для построения канала индекса (спреда)
        public IntOptimProperty ChannelInd_0_MA_1_Bollinger = new IntOptimProperty(0,0,1,1);

        // когда выходим из позиции: по центру канала (0) или по границе канала (1)
        public IntOptimProperty MethodExit_0_Center_1_Boundary = new IntOptimProperty(0,0,1,1);

        // максимальное количество открываемых позиций в одном направлении
        public IntOptimProperty MaxPositionsCount = new IntOptimProperty(2, 1, 5, 1);

        // отклонение индекса для дополнительного входа в позицию в процентах
        public OptimProperty DeviationIndexForAddEnter = new OptimProperty(1, 0.1, 3, 0.1);

        // размер комиссии
        public OptimProperty CommissionPct = new OptimProperty(0.1, 0, 0.2, 0.01);

        #endregion

        #region=== Внутренние переменные робота ===
        // контекст в TSLab
        private IContext _ctx;

        // инструменты в TSLab
        private ISecurity _sec1;    // торгуемый инструмент
        private ISecurity _sec2;
        private ISecurity _sec3;
        private ISecurity _sec4;

        // коэффициенты для построения индекса (спреда)
        private double coef1 = 10;
        private double coef2 = 1;
        private double coef3 = 100;
        private double coef4 = 5000;

        // размер тика для инструментов
        private double _tick1 = 0.01;
        private double _tick2 = 0.01;
        private double _tick3 = 0.01;
        private double _tick4 = 0.01;

        // стартовый бар
        private int _startBar;

        // количество бар для расчета
        private int _barsCount;

        // индикаторы робота
        private IList<double> _index;
        private IList<double> _indexMa;
        private IList<double> _indexStDev;
        private IList<double> _upBollinger;
        private IList<double> _downBollinger;
        private IList<double> _upChannel;
        private IList<double> _downChannel;

        // последние значения
        private double _lastPrice;
        private double _lastIndex;
        private double _lastMa;
        private double _lastUpChannel;
        private double _lastDownChannel;

        // массивы для дополнительного вывода сигналов на график
        double[] _arrSignalLE;
        double[] _arrSignalSE;

        #endregion

        public void Execute(IContext ctx, ISecurity sec1, ISecurity sec2, ISecurity sec3, ISecurity sec4)
        {
            // запуск таймера для определения времени выполнения скрипта
            var sw = Stopwatch.StartNew();

            _ctx = ctx;
            _sec1 = sec1;
            _sec2 = sec2;
            _sec3 = sec3;
            _sec4 = sec4;

            if (ctx.Runtime.IsAgentMode)
            {
                _tick1 = sec1.Tick;
                _tick2 = sec2.Tick;
                _tick3 = sec3.Tick;
                _tick4 = sec4.Tick;
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

            // расчет начального и конечного бара TSLab
            if (CalcBarsTSL() == false)
            {
                return;
            }

            // расчет индикаторов робота
            CalcIndicators();

            // торговый цикл
            TradeLogic(sec1.ClosePrices);

            // вывод графиков
            DrawGraph();

            // пишем в лог только в режиме Лаборатория
            if (!ctx.Runtime.IsAgentMode)
                ctx.LogInfo($"Скрипт выполнен за время: {sw.Elapsed}");
        }

        /// <summary>
        /// Расчет начального и конечного бара
        /// </summary>
        private bool CalcBarsTSL()
        {
            if (_sec1.Bars.Count != _sec2.Bars.Count ||
                _sec1.Bars.Count != _sec3.Bars.Count ||
                _sec1.Bars.Count != _sec4.Bars.Count)
            {
                return false;
            }

            // последний сформировавшийся бар для расчетов
            _barsCount = _sec1.Bars.Count;

            if (!_ctx.IsLastBarUsed)
            {
                _barsCount--;
            }

            if (_barsCount <= 0)
                throw new ArgumentException($"_barsCount = {_barsCount} <= 0");

            // первый используемый для расчетов бар
            _startBar = Math.Max(LenghtMa.Value + 30, LenghtStDev.Value + 30);
            _startBar = Math.Max(_startBar, LenghtBollinger.Value + 30);
            _startBar = Math.Max(_startBar, _ctx.TradeFromBar);
            
            return true;
        }

        /// <summary>
        /// Расчет индикаторов робота c кэшированием
        /// </summary>
        private void CalcIndicators()
        {
            // вычисляем индекс (спред), используем отношение
            var indexPart1 = _sec1.ClosePrices.MultiplyConst(coef1);
            var indexPart2 = _sec2.ClosePrices.MultiplyConst(coef2);
            var indexPart3 = _sec3.ClosePrices.MultiplyConst(coef3);
            var indexPart4 = _sec4.ClosePrices.MultiplyConst(coef4);
            var indexNumerator = indexPart1.Add(indexPart2).Add(indexPart3).Add(indexPart3);
            _index = indexNumerator.Divide(_sec1.ClosePrices);

            // усредняем спред
            if (ChannelInd_0_MA_1_Bollinger == 0)
            {
                _indexMa = _ctx.GetData("indexMa", new string[] { LenghtMa.ToString() },
                    () => Series.SMA(_index, LenghtMa.Value));

                _indexStDev = _ctx.GetData("indexStDev", new string[] { LenghtStDev.ToString() },
                    () => Series.StDev(_index, LenghtStDev.Value));

                _upChannel = _ctx.GetData("upChannel",
                    new string[] {LenghtMa.Value.ToString(), LenghtStDev.Value.ToString(), MultiplyStDev.Value.ToString()},
                    () => Series.SMA(_index, LenghtMa.Value).Add(Series.StDev(_index, LenghtStDev.Value).MultiplyConst(MultiplyStDev.Value)));

                _downChannel = _ctx.GetData("downChannel",
                    new string[] { LenghtMa.Value.ToString(), LenghtStDev.Value.ToString(), MultiplyStDev.Value.ToString() },
                    () => Series.SMA(_index, LenghtMa.Value).Subtract( Series.StDev(_index, LenghtStDev.Value).MultiplyConst(MultiplyStDev.Value) ));

            }
            else
            {
                _indexMa = _ctx.GetData("indexMa", new string[] { LenghtMa.ToString() },
                    () => Series.SMA(_index, LenghtMa.Value));

                _upChannel = _ctx.GetData("upBollinger",
                    new string[] { LenghtBollinger.Value.ToString(), DeviationBollinger.Value.ToString() },
                    () => Series.BollingerBands(_index, LenghtBollinger.Value, DeviationBollinger.Value, true));

                _downChannel = _ctx.GetData("downBollinger",
                    new string[] { LenghtBollinger.Value.ToString(), DeviationBollinger.Value.ToString() },
                    () => Series.BollingerBands(_index, LenghtBollinger.Value, DeviationBollinger.Value, false));
            }

            // создаем массивы для дополнительного вывода торговых сигналов на графики
            _arrSignalLE = new double[_barsCount];
            _arrSignalSE = new double[_barsCount];

        }

        private void TradeLogic(IList<double> closePrices)
        {

            for (int i = _startBar; i < _barsCount; i++)
            {
                _lastPrice = closePrices[i];
                _lastIndex = _index[i];
                _lastUpChannel = _upChannel[i];
                _lastDownChannel = _downChannel[i];
                _lastMa = _indexMa[i];

                // проверка на корректность цены
                if (_lastPrice <= 0 || _lastIndex <= 0)
                {
                    _ctx.LogError("TradeCicle: некорректное значение цены инструмента или индекса");
                    return;
                }

                var currentMarketFaze = GetMarketFaze();

                // проверка условий закрытия позиций
                CheckClosingPositions(i, currentMarketFaze);

                // в зависимости от фазы рынка проверка условий открытия позиций
                if (currentMarketFaze == MarketFaze.Upper)
                {
                    TryOpenLong(i);
                }
                else if (currentMarketFaze == MarketFaze.Lower)
                {
                    TryOpenShort(i);
                }
            }
        }

        /// <summary>
        /// Получить текущую фазу рынка
        /// </summary>
        /// <returns></returns>
        private MarketFaze GetMarketFaze()
        {
            MarketFaze currentMarketFaze;

            if (_lastIndex > _lastUpChannel)
            {
                currentMarketFaze = MarketFaze.Upper;
            }
            else if (_lastIndex > _lastMa && _lastIndex <= _lastUpChannel)
            {
                currentMarketFaze = MarketFaze.Up;
            }
            else if (_lastIndex <= _lastMa && _lastIndex >= _lastDownChannel)
            {
                currentMarketFaze = MarketFaze.Low;
            }
            else if (_lastIndex < _lastDownChannel)
            {
                currentMarketFaze = MarketFaze.Lower;
            }
            else
            {
                currentMarketFaze = MarketFaze.Nothing;
            }

            return currentMarketFaze;
        }

        private void CheckClosingPositions(int bar, MarketFaze currentMarketFaze)
        {
            // получаем все открытые позиции 
            var positions = _sec1.Positions.GetActiveForBar(bar).ToList();

            // для каждой позиции проверяем условия её закрытия
            for (int i = 0; positions != null && i < positions.Count; i++)
            {
                // если позиция уже закрывается, то ничего с ней не делаем (применимо только для реальной торговли)
                if (positions[i].PositionState  == PositionState.HaveCloseSignal)
                {
                    continue;
                }

                // если позиция лонг
                if (positions[i].IsLong)
                {
                    if (MethodExit_0_Center_1_Boundary.Value == 1 &&
                        currentMarketFaze == MarketFaze.Lower)
                    {
                        // закрываем позицию по лимиту
                        CloseLong(bar, positions[i]);
                    }
                    else if (MethodExit_0_Center_1_Boundary.Value == 0 &&
                            currentMarketFaze == MarketFaze.Low)
                    {
                        // закрываем позицию по лимиту
                        CloseLong(bar, positions[i]);
                    }
                }
                // если позиция шорт
                else if (positions[i].IsShort)
                {
                    if (MethodExit_0_Center_1_Boundary.Value == 1 &&
                        currentMarketFaze == MarketFaze.Upper)
                    {
                        // закрываем позицию по лимиту
                        CloseShort(bar, positions[i]);
                    }
                    else if (MethodExit_0_Center_1_Boundary.Value == 0 &&
                             currentMarketFaze == MarketFaze.Up)
                    {
                        // закрываем позицию по лимиту
                        CloseShort(bar, positions[i]);
                    }
                }
            }
        }

        private void CloseShort(int bar, IPosition shortPosition)
        {
            // если позиция не активна на текущем баре, то ничего не делаем
            if (!shortPosition.IsActiveForBar(bar))
            {
                return;
            }

            // если позиция уже закрывается, то ничего не делаем (только для реальной торговли)
            if (shortPosition.PositionState == PositionState.HaveCloseSignal)
            {
                return;
            }

            string signalName = shortPosition.EntrySignalName.Replace('E', 'X');

            // выставляем лимитную заявку на закрытие позиции шорт (покупаем)
            shortPosition.CloseAtPrice(bar+1,
                _lastPrice + Slippage * _tick1,
                signalName);
        }

        private void CloseLong(int bar, IPosition longPosition)
        {
            // если позиция не активна на текущем баре, то ничего не делаем
            if (!longPosition.IsActiveForBar(bar))
            {
                return;
            }

            // если позиция уже закрывается, то ничего не делаем (только для реальной торговли)
            if (longPosition.PositionState == PositionState.HaveCloseSignal)
            {
                return;
            }

            string signalName = longPosition.EntrySignalName.Replace('E', 'X');
            
            // выставляем лимитную заявку на закрытие позиции лонг (продаем)
            longPosition.CloseAtPrice(bar+1,
                _lastPrice - Slippage * _tick1,
                signalName);
        }

        private void TryOpenLong(int bar)
        {
            // получаем все позиции лонг
            var longPositions = _sec1.Positions.GetActiveForBar(bar).ToList().FindAll(pos=>pos.IsLong);

            // Если лонг позиций нет, то открываем лонг позицию
            if (longPositions.Count == 0)
            {
                OpenLong(bar, "LE"+ longPositions.Count.ToString());
            }

            // если есть лонг позиции, но их количество меньше MaxPositionCount,
            // то пробуем открыть дополнительную лонг позицию
            else if (longPositions.Count < MaxPositionsCount)
            {
                // получаем значение индекса, при котором была открыта предыдущая шорт позиция
                double indexForPrevPosition;
                try
                {
                    indexForPrevPosition = Convert.ToDouble(longPositions[longPositions.Count - 1].EntryNotes);
                }
                catch (Exception e)
                {
                    _ctx.LogError("TryOpenLong: не могу получить значение индекса," +
                                             " при котором была открыта предыдущая лонг позиция");
                    return;
                }

                // если текущее значение индекса стало больше на заданную величину,
                // чем индекс для предыдущей открытой позиции,
                // то открываем дополнительную лонг позицию
                if (_lastIndex > indexForPrevPosition * (1 + DeviationIndexForAddEnter.Value / 100.0))
                {
                    OpenLong(bar, "LE" + longPositions.Count.ToString());
                }
            }
        }

        private void OpenLong(int bar, string signal)
        {
            double pricePosition = _lastPrice + Slippage * _tick1;
            double volumePosition = GetVolume(_sec1, bar, pricePosition);

            _sec1.Positions.BuyAtPrice(bar+1,volumePosition, pricePosition, signal, _lastIndex.ToString());
        }

        private void TryOpenShort(int bar)
        {
            // получаем все шорт позиции
            var shortPositions = _sec1.Positions.GetActiveForBar(bar).ToList().FindAll(pos=>pos.IsShort);

            // Если шорт позиций нет, то открываем шорт позицию
            if (shortPositions.Count == 0)
            {
                OpenShort(bar, "SE" + shortPositions.Count.ToString());
            }
            // если есть шорт позиции, но их количество меньше MaxPositionCount,
            // то пробуем открыть дополнительную шорт позицию
            else if (shortPositions.Count < MaxPositionsCount)
            {
                // получаем значение индекса, при котором была открыта предыдущая шорт позиция
                double indexForPrevPosition;

                try
                {
                    indexForPrevPosition = Convert.ToDouble(shortPositions[shortPositions.Count - 1].EntryNotes);
                }
                catch (Exception e)
                {
                    _ctx.LogError("TryOpenShort: не могу получить значение индекса," + 
                                  " при котором была открыта предыдущая шорт позиция");
                    return;
                }

                // если текущее значение индекса стало меньше на заданную величину,
                // чем индекс для предыдущей открытой позиции, то открываем дополнительную шорт позицию
                if (_lastIndex < indexForPrevPosition * (1 - DeviationIndexForAddEnter.Value / 100.0))
                {
                    OpenShort(bar, "SE" + shortPositions.Count.ToString());
                }
            }
        }

        private void OpenShort(int bar, string signal)
        {
            double pricePosition = _lastPrice - Slippage * _tick1;
            double volumePosition = GetVolume(_sec1, bar, pricePosition);

            _sec1.Positions.SellAtPrice(bar+1, volumePosition, pricePosition, signal, _lastIndex.ToString());
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

            // если включен режим фиксированного объема входа, то возвращаем настроечный параметр FixedVolume
            if (OnFixedVolume.Value)
            {
                return FixedVolume.Value;
            }

            // получаем размер депозита
            double depositSize = GetDepositSize(bar);

            // рассчитываем объем входа в позицию и округляем до заданного количества знаков после запятой
            double volume = Math.Round (depositSize / price * VolumePctOfDeposit.Value / 100.0);

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

            // вычисляем текущее значение депозита
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


            IGraphPane pane3 = _ctx.CreateGraphPane("Index", "Index");
            Color colorCandle3 = ScriptColors.Black;
            var graphSpred = pane3.AddList("Index",
                _index,
                ListStyles.LINE,
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

            var graphIndexMa = pane3.AddList($"MA {LenghtMa.Value}",
                _indexMa,
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

    // индикатор для построения канала спреда
    public enum ChannelIndicator
    {
        MA_StDev = 0,
        Bollinger = 1
    }

    // фаза рынка
    public enum MarketFaze
    {
        Upper,
        Up,
        Low,
        Lower,
        Nothing
    }

    public enum MethodOfExit
    {
        BoundaryChannel,
        CenterChannel
    }
}
