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
using TSLab.Script.Realtime;

namespace AlligatorTrend
{
    public class AlligatorTrend : IExternalScript
    {
        #region === Параметры робота ===
        // параметры аллигатора
        public IntOptimProperty AlligatorFastLenght = new IntOptimProperty(25, 10, 100, 10);
        public IntOptimProperty AlligatorMiddleLenght = new IntOptimProperty(50, 10, 100, 10);
        public IntOptimProperty AlligatorSlowLenght = new IntOptimProperty(100, 10, 200, 10);
        public IntOptimProperty AlligatorFastShift = new IntOptimProperty(5, 5, 15, 2);
        public IntOptimProperty AlligatorMiddleShift = new IntOptimProperty(8, 8, 25, 2);
        public IntOptimProperty AlligatorSlowShift = new IntOptimProperty(13, 13, 40, 2);

        // максимальное количество позиций, открываемых роботом
        public IntOptimProperty MaxPositionsCount = new IntOptimProperty(1, 1, 10, 1);
        
        // отклонение цены для входа в дополнительную позицию
        public OptimProperty DeviationPriceForAddEnter = new OptimProperty(1, 1, 10, 1);

        // объем входа в позицию
        public OptimProperty Volume = new OptimProperty(0.1, 0.1, 100, 0.1);

        // проскальзывание
        public IntOptimProperty Slippage = new IntOptimProperty(100, 0, 500, 10);

        // размер комиссии
        public OptimProperty CommissionPct = new OptimProperty(0.1, 0, 0.2, 0.01);
        
        // шаг цены
        public OptimProperty PriceStep = new OptimProperty(0.01, 0.001, 1, 0.001);

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

        // линии аллигатора
        private IList<double> _fastAlligator;
        private IList<double> _middleAlligator;
        private IList<double> _slowAlligator;

        // последние значения цены и линий аллигатора
        private double _lastPrice;
        private double _lastFastAlligator;
        private double _lastMiddleAlligator;
        private double _lastSlowAlligator;
        private double _prevFastAlligator;
        private double _prevMiddleAlligator;
        private double _prevSlowAlligator;

        // торговые сигналы
        private bool _signalLE = false;
        private bool _signalSE = false;
        private bool _signalLX = false;
        private bool _signalSX = false;


        // для вывода сигналов на график
        double[] _arrSignalLE;
        double[] _arrSignalSE;
        double[] _arrSignalLX;
        double[] _arrSignalSX;
        double[] _arrVolume;

        #endregion

        public void Execute(IContext ctx, ISecurity sec)
        {
            // запуск таймера для определения времени выполнения скрипта
            var sw = Stopwatch.StartNew();

            // если длина линий аллигатора противоречит логики индикатора, то ничего не делаем
            if (AlligatorFastLenght.Value > AlligatorMiddleLenght.Value ||
                AlligatorFastLenght.Value > AlligatorSlowLenght.Value ||
                AlligatorMiddleLenght.Value > AlligatorSlowLenght.Value)
            {
                return;
            }

            // если смещение линий аллигатора противоречит логики индикатора, то ничего не делаем
            if (AlligatorFastShift.Value > AlligatorMiddleShift.Value ||
                AlligatorFastShift.Value > AlligatorSlowShift.Value ||
                AlligatorMiddleShift.Value > AlligatorSlowShift.Value)
            {
                return;
            }

            _ctx = ctx;
            _sec = sec;

            if (ctx.Runtime.IsAgentMode)
                _tick = sec.Tick;
            else
                _tick = PriceStep.Value;

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

            if (_barsCount <= 0)
                throw new ArgumentException($"_barsCount = {_barsCount} <= 0");

            // первый используемый для расчетов бар
            _startBar = Math.Max(AlligatorSlowLenght.Value + 5, _ctx.TradeFromBar);

            // создаем массивы для дополнительного вывода торговых сигналов на графики
            _arrSignalLE = new double[_barsCount];
            _arrSignalSE = new double[_barsCount];
            _arrSignalLX = new double[_barsCount];
            _arrSignalSX = new double[_barsCount];
        }

        /// <summary>
        /// Расчет индикаторов робота c кэшированием
        /// </summary>
        private void CalcIndicators()
        {
            IList<double> medianPrices = Series.MedianPrice(_sec.Bars);

            _fastAlligator = _ctx.GetData("fastAlligator",
                new string[] { AlligatorFastLenght.ToString() },
                () => Series.SMMA(medianPrices, AlligatorFastLenght.Value, AlligatorFastShift.Value));

            _middleAlligator = _ctx.GetData("middleAlligator",
                new string[] { AlligatorMiddleLenght.ToString() },
                () => Series.SMMA(medianPrices, AlligatorMiddleLenght.Value, AlligatorMiddleShift.Value));

            _slowAlligator = _ctx.GetData("slowAlligator",
                new string[] { AlligatorSlowLenght.ToString()},
                () => Series.SMMA(medianPrices, AlligatorSlowLenght.Value, AlligatorSlowShift.Value));
        }

        /// <summary>
        /// Основной торговый цикл 
        /// </summary>
        /// <param name="closePrices"></param>
        private void TradeCicle(IList<double> closePrices)
        {
            for (int i = _startBar; i < _barsCount; i++)
            {
                _lastPrice = closePrices[i];
                _lastFastAlligator = _fastAlligator[i];
                _lastMiddleAlligator = _middleAlligator[i];
                _lastSlowAlligator = _slowAlligator[i];

                // проверка на корректность цены
                if (_lastPrice <= 0 || _lastFastAlligator <= 0 || _lastMiddleAlligator <=0 || _lastSlowAlligator <= 0)
                {
                    _ctx.LogError("TradeCicle: некорректное значение цены инструмента или индекса");
                    return;
                }

                // сбрасываем торговые сигналы, используемые для вывода на график
                _signalLE = false;
                _signalLX = false;
                _signalSE = false;
                _signalSX = false;

                // получаем все открытые позиции 
                var positions = _sec.Positions.GetActiveForBar(i).ToList();

                // для каждой позиции проверяем условия её закрытия
                for (int j= 0; positions != null && j < positions.Count; j++)
                {
                    // если позиция уже закрывается, то ничего с ней не делаем (применимо только для реальной торговли)
                    if (positions[j].PositionState == PositionState.HaveCloseSignal)
                    {
                        continue;
                    }

                    // проверяем условие закрытие лонг позиции
                    if (positions[j].IsLong &&
                        (_lastFastAlligator < _lastMiddleAlligator  || _lastMiddleAlligator < _lastSlowAlligator))
                    {
                        // торговый сигнал для вывода на график
                        _signalLX = true;

                        // закрываем позицию по лимиту
                        CloseLong(i, positions[j]);
                    }
                    // проверяем условие закрытие шорт позиции
                    else if (positions[j].IsShort &&
                             (_lastFastAlligator > _lastMiddleAlligator || _lastMiddleAlligator > _lastSlowAlligator))
                    {
                        // сигнал для вывода на график
                        _signalSX = true;

                        // закрываем позицию по лимиту
                        CloseShort(i, positions[j]);
                    }
                }

                // проверка условия открытия позиции лонг
                if (_lastPrice > _lastFastAlligator &&
                    _lastFastAlligator > _lastMiddleAlligator &&
                    _lastMiddleAlligator > _lastSlowAlligator)
                {
                    TryOpenLong(i);
                }
                // проверка условия открытия позиции шорт
                else if (_lastPrice < _lastFastAlligator &&
                         _lastFastAlligator < _lastMiddleAlligator &&
                         _lastMiddleAlligator < _lastSlowAlligator)
                {
                    TryOpenShort(i);
                }

                // сохраняем сигналы для вывода на график
                _arrSignalLE[i] = (_signalLE) ? 1 : 0;
                _arrSignalSE[i] = (_signalSE) ? 1 : 0;
                _arrSignalLX[i] = (_signalLX) ? 1 : 0;
                _arrSignalSX[i] = (_signalSX) ? 1 : 0;
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
            shortPosition.CloseAtPrice(bar + 1,
                _lastPrice + Slippage * _tick,
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
            longPosition.CloseAtPrice(bar + 1,
                _lastPrice - Slippage * _tick,
                signalName);
        }

        private void TryOpenLong(int bar)
        {
            // получаем все позиции лонг
            var longPositions = _sec.Positions.GetActiveForBar(bar).ToList().FindAll(pos => pos.IsLong);

            // Если лонг позиций нет, то открываем лонг позицию
            if (longPositions.Count == 0)
            {
                // торговый сигнал для вывода на график
                _signalLE = true;

                OpenLong(bar, "LE" + longPositions.Count.ToString());
            }

            // если есть лонг позиции, но их количество меньше MaxPositionCount,
            // то пробуем открыть дополнительную лонг позицию
            else if (longPositions.Count < MaxPositionsCount)
            {
                // получаем значение цены, при которой была открыта предыдущая лонг позиция
                double pricePrevPosition;
                try
                {
                    pricePrevPosition = Convert.ToDouble(longPositions[longPositions.Count - 1].EntryNotes);
                }
                catch (Exception e)
                {
                    _ctx.LogError("TryOpenLong: не могу получить значение цены входа," +
                                             " для предыдущей лонг позиции");
                    return;
                }

                // если текущее значение цены стало больше на заданную величину,
                // чем цена для предыдущей открытой позиции,
                // то открываем дополнительную лонг позицию
                if (_lastPrice > pricePrevPosition * (1 + DeviationPriceForAddEnter.Value / 100.0))
                {
                    // торговый сигнал для вывода на график
                    _signalLE = true;

                    OpenLong(bar, "LE" + longPositions.Count.ToString());
                }
            }
        }

        private void OpenLong(int bar, string signal)
        {
            double pricePosition = _lastPrice + Slippage * _tick;

            _sec.Positions.BuyAtPrice(bar + 1, Volume.Value, pricePosition, signal, _lastPrice.ToString());
        }

        private void TryOpenShort(int bar)
        {
            // получаем все шорт позиции
            var shortPositions = _sec.Positions.GetActiveForBar(bar).ToList().FindAll(pos => pos.IsShort);

            // Если шорт позиций нет, то открываем шорт позицию
            if (shortPositions.Count == 0)
            {
                _signalSE = true;
                OpenShort(bar, "SE" + shortPositions.Count.ToString());
            }
            // если есть шорт позиции, но их количество меньше MaxPositionCount,
            // то пробуем открыть дополнительную шорт позицию
            else if (shortPositions.Count < MaxPositionsCount)
            {
                // получаем значение цены, при которой была открыта предыдущая шорт позиция
                double pricePrevPosition;

                try
                {
                    pricePrevPosition = Convert.ToDouble(shortPositions[shortPositions.Count - 1].EntryNotes);
                }
                catch (Exception e)
                {
                    _ctx.LogError("TryOpenShort: не могу получить значение цены входа" +
                                  " для предыдущей шорт позиции");
                    return;
                }

                // если текущее значение индекса стало меньше на заданную величину,
                // чем индекс для предыдущей открытой позиции, то открываем дополнительную шорт позицию
                if (_lastPrice < pricePrevPosition * (1 - DeviationPriceForAddEnter.Value / 100.0))
                {
                    _signalSE = true;
                    OpenShort(bar, "SE" + shortPositions.Count.ToString());
                }
            }
        }

        private void OpenShort(int bar, string signal)
        {
            double pricePosition = _lastPrice - Slippage * _tick;

            _sec.Positions.SellAtPrice(bar + 1, Volume, pricePosition, signal, _lastPrice.ToString());
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

            var graphFastAlligator = pane.AddList($"fastAlligator ({AlligatorFastLenght}-{AlligatorFastShift})",
                _fastAlligator,
                ListStyles.LINE,
                ScriptColors.Red,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var graphMiddleAlligator = pane.AddList($"middleAlligator ({AlligatorMiddleLenght}-{AlligatorMiddleShift})",
                _middleAlligator,
                ListStyles.LINE,
                ScriptColors.Green,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var graphSlowAlligator = pane.AddList($"slowAlligator ({AlligatorSlowLenght}-{AlligatorSlowShift})",
                _slowAlligator,
                ListStyles.LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

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
            /*
            var graphSignalLX = pane.AddList("SignalLX",
                _arrSignalLX,
                ListStyles.HISTOHRAM,
                ScriptColors.Yellow,
                LineStyles.SOLID,
                PaneSides.LEFT);

            var graphignalSX = pane.AddList("SignalSX",
                _arrSignalSX,
                ListStyles.HISTOHRAM,
                ScriptColors.Magenta,
                LineStyles.SOLID,
                PaneSides.LEFT);

            /*
            // раскрашиваем бары в зависимости от наличия позиции
            // и определяем объем входа в позицию на каждом баре
            _arrVolume = new double[_barsCount];
            for (int i = 0; i < _barsCount; i++)
            {
                var activePositions = _sec.Positions.GetActiveForBar(i).ToList();
                graphSec.SetColor(i, activePositions.Any() ? ScriptColors.Black : ScriptColors.DarkGray);

                foreach (var pos in activePositions)
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
            */

        }
    }

}
