using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;

namespace TSLabBot
{
    /// <summary>
    /// Трендовый бот по пробою канала Дончиана.
    /// Входит только одной позицией по реверсной системе.
    /// При достижении заданного профита позиция переводится в безубыток
    /// </summary>
    public class BotDonchianTrend : IExternalScript
    {
        #region === Настроечные параметры бота ===
        // режим работы бота
        //public EnumOptimProperty RegimeBot = new EnumOptimProperty(TSLabBot.RegimeBot.On);
        RegimeBot _regimeBot = RegimeBot.On;

        // период канала
        public IntOptimProperty PeriodChannel = new IntOptimProperty(50, 10, 100, 5);

        // проскальзывание в шагах цены
        public IntOptimProperty Slippage = new IntOptimProperty(20, 1, 500, 1);

        // включить перевод в безубыток
        public BoolOptimProperty OnBreakevenStop = new BoolOptimProperty(false);

        // размер профита для перевода позиции в безубыток
        public IntOptimProperty ProfitForBreakevenPct = new IntOptimProperty(5, 1, 10, 1);

        // включить стоп лосс
        public BoolOptimProperty OnStopLoss = new BoolOptimProperty(false);

        // тип стоп-лосса 
        StopType _stopType = StopType.Stop;

        // размер стоп-лоса в процентах
        public IntOptimProperty StopLossPct = new IntOptimProperty(5, 1, 10, 1);

        // размер включение трейлинг-стопа в процентах
        public IntOptimProperty TrailLossEnablePct = new IntOptimProperty(5, 1, 10, 1);

        // размер трейлинг-стопа в процентах
        public IntOptimProperty TrailLossPct = new IntOptimProperty(5, 1, 10, 1);

        // включить тейк профит
        public BoolOptimProperty OnTakeProfit = new BoolOptimProperty(false);

        // размер тейк-профита в процентах
        public IntOptimProperty TakeProfitPct = new IntOptimProperty(5, 1, 10, 1);
        #endregion

        #region === Общие параметры бота ===
        // константа для сравнения чисел c плавающей точкой
        private const double Eps = 1E-7;

        // Обработчик трейлинг-стопа
        private TrailStop _trailStopHnd;
        #endregion

        /// <summary>
        /// Основной метод бота
        /// </summary>
        /// <param name="ctx">Контекст TSLab</param>
        /// <param name="sec">Инструмент</param>
        public void Execute(IContext ctx, ISecurity sec)
        {
            // таймер для определения времени выполнения скрипта
            var sw = Stopwatch.StartNew();

            #region расчет индикаторов
            IList<double> upChannel = ctx.GetData("UpChannel", 
                new string[] { PeriodChannel.ToString() },
                () => Series.Highest(sec.HighPrices, PeriodChannel));

            IList<double> downChannel = ctx.GetData("DownChannel",
                new string[] { PeriodChannel.ToString() },
                () => Series.Lowest(sec.LowPrices, PeriodChannel));
            #endregion

            #region первый и последний бар для расчетов
            // первый бар, используемый для расчетов
            int startBar = Math.Max(PeriodChannel + 1, ctx.TradeFromBar);

            // последний бар, используемый для расчетов
            var barsCount = sec.Bars.Count;
            if (!ctx.IsLastBarClosed)
                barsCount--;
            #endregion

            // цены закрытия инструмента
            var closePrices = sec.ClosePrices;

            #region Создание объекта для трейлинг стопа
            _trailStopHnd = new TrailStop()
            {
                StopLoss = StopLossPct,
                TrailEnable = TrailLossEnablePct,
                TrailLoss = TrailLossPct
            };
            #endregion


            #region Торговый цикл
            for (int i = startBar; i < barsCount; i++)
            {
                #region Формируем торговые сигналы
                // сигнал входа в лонг, сигнал активен в течение 2-х бар при выхода цены закрытия из канала
                bool signalLE = closePrices[i] > upChannel[i - 1] && closePrices[i - 2] <= upChannel[i - 3];

                // сигнал входа в шорт, сигнал активен в течение 2-х бар при выхода цены закрытия из канала
                bool signalSE = closePrices[i] < downChannel[i - 1] && closePrices[i - 2] >= downChannel[i - 3];

                // сигнал выхода из лонга
                bool signalLX = closePrices[i] < downChannel[i - 1];

                // сигнал выхода из шорта
                bool signalSX = closePrices[i] > upChannel[i - 1];
                #endregion

                #region Получаем активные позиции
                var lePosition = sec.Positions.GetLastActiveForSignal("LE", i);
                var sePosition = sec.Positions.GetLastActiveForSignal("SE", i);
                var openPositions = sec.Positions.GetActiveForBar(i).ToList();
                #endregion

                #region Выход из позиции, реверс позиции, установка стоп-лосса и тейк-профита
                if (openPositions.Count != 0)
                {
                    foreach (IPosition pos in openPositions)
                    {
                        switch (pos.EntrySignalName)
                        {
                            #region обработка позиции лонг LE
                            case "LE":
                                //условие выхода из лонга
                                if (signalLX)
                                {
                                    //закрытие лонга по лимиту
                                    pos.CloseAtPriceSlip(i + 1, closePrices[i], Slippage, "LX");

                                    // условие реверса позиции
                                    if (_regimeBot != RegimeBot.OnlyShort &&
                                        _regimeBot != RegimeBot.OnlyClosePosition)
                                    {
                                        // открытие шорта по лимиту для реверса позиции
                                        sec.SellAtPriceSlip(i + 1, 1, closePrices[i], Slippage, "SE");
                                    }
                                }
                                // если не было сигнала выхода, то выставляем стопы и профиты
                                else
                                {
                                    // выставление стоп-лосса
                                    if (OnStopLoss || OnBreakevenStop)
                                        SetStopLoss(pos, i);

                                    // выставление тейк-профотита
                                    if (OnTakeProfit)
                                        SetTakeProfit(pos, i);
                                }
                                break;
                            #endregion

                            #region обработка позиции шорт SE
                            case "SE":
                                // условие выхода из шорта
                                if (signalSX)
                                {
                                    // закрытие шорта по лимиту
                                    pos.CloseAtPriceSlip(i + 1, closePrices[i], Slippage, "SX");

                                    // условие реверса позиции
                                    if (_regimeBot != RegimeBot.OnlyShort &&
                                        _regimeBot != RegimeBot.OnlyClosePosition)
                                    {
                                        // открытие лонга по лимиту для реверса позиции
                                        sec.BuyAtPriceSlip(i + 1, 1, closePrices[i], Slippage, "LE");
                                    }
                                }
                                // если не было сигналов на выход, то выставляем стоп-лосс и тейк-профит
                                else
                                {
                                    // выставление стопа-лосса
                                    if (OnStopLoss || OnBreakevenStop)
                                        SetStopLoss(pos, i);

                                    // выставление тейк-профита
                                    if (OnTakeProfit)
                                        SetTakeProfit(pos, i);
                                }
                                break;
                            #endregion

                            #region обработка прочих позиций
                            default:
                                ctx.Log($"Обработка прочих позиций {pos.EntrySignalName}", MessageType.Warning);
                                break;
                                #endregion
                        }
                    }
                }
                #endregion

                if (_regimeBot == RegimeBot.OnlyClosePosition)
                    continue;

                #region Вход в первую позицию
                // если нет ни одной активной позиции, то проверяем условие входа в первую позицию
                if (openPositions.Count == 0)
                {
                    // лонг
                    if (signalLE && _regimeBot != RegimeBot.OnlyShort)
                    {
                        sec.BuyAtPriceSlip(i + 1, 1, closePrices[i], Slippage, "LE");
                    }
                    // шорт
                    else if (signalSE && _regimeBot != RegimeBot.OnlyLong)
                    {
                        sec.SellAtPriceSlip(i + 1, 1, closePrices[i], Slippage, "SE");
                    }
                }
                #endregion
            }
            #endregion

            #region Прорисовка графиков
            // Если идет процесс оптимизации, то графики рисовать не нужно, это замедляет работу
            if (ctx.IsOptimization)
            {
                return;
            }

            IGraphPane pane = ctx.First ?? ctx.CreateGraphPane("First", "First");
            Color colorCandle = ScriptColors.Black;
            pane.AddList(sec.Symbol, sec, CandleStyles.BAR_CANDLE, colorCandle, PaneSides.RIGHT);
            var lineUpChannel = pane.AddList(string.Format($"UpChannel ({PeriodChannel,0})"),
                upChannel,
                ListStyles.LINE,
                ScriptColors.Red,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            //lineUpChannel.Thickness = 2;

            var lineDownChannel = pane.AddList(string.Format($"DwChannel ({PeriodChannel,0}"),
                downChannel,
                ListStyles.LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            //lineDownChannel.Thickness = 2;

            #endregion

            #region Вывод в лог
            // Вывод в лог времени выполнения скрипта только в режиме Лаборатория
            if (!ctx.Runtime.IsAgentMode)
                ctx.Log($"Скрипт выполнен за время: {sw.Elapsed}", MessageType.Info, true);
            #endregion
        }

        /// <summary>
        /// Выставление стоп-лосса для позиции, включая стоп-лосс для перевода позициив безубыток
        /// </summary>
        /// <param name="pos">Позиция, для которой выставляется стоп-лосс</param>
        public void SetStopLoss(IPosition pos, int numBar)
        {
            if (pos == null || !pos.IsActiveForBar(numBar))
                return;

            // предельно минимальное значение стоп-лоса для позиции
            double minLimitStopLoss = (pos.IsLong) ? 0 : double.MaxValue;

            // предыдущее значение стоп-лосса для позиции
            double prevStopPrice = (pos.GetStop(numBar) != 0) ? pos.GetStop(numBar) : minLimitStopLoss;

            // новое значение стоп-лосса
            double stopPrice = 0;

            // значение стоп-лосса для перевода позиции в безубыток
            double breakevenStopPrice;

            if (OnStopLoss || OnBreakevenStop)
            {
                // вычисляем обычный стоп-лосс
                if (OnStopLoss && _stopType == StopType.Stop)
                    stopPrice = pos.GetStopPrice(StopLossPct);
                // вычисляем трейл-стоп
                else if (OnStopLoss && _stopType == StopType.Trail)
                    stopPrice = _trailStopHnd.Execute(pos, numBar);
                
                // вычисляем стоп для перевода позиции в безубыток
                if (OnBreakevenStop && pos.CurrentProfitPct(numBar) > ProfitForBreakevenPct)
                {
                    breakevenStopPrice = (pos.IsLong)
                        ? pos.EntryPrice + 10 * pos.Security.Tick
                        : pos.EntryPrice - 10 * pos.Security.Tick;

                    if (pos.IsLong)
                        stopPrice = Math.Max(stopPrice, breakevenStopPrice);
                    else if (pos.IsShort)
                        stopPrice = (stopPrice == 0) ? breakevenStopPrice : (Math.Min(stopPrice, breakevenStopPrice));
                }

                // Сравниваем с предыдущим значением стопа
                // и берем максимальное или минимальное значение в зависимости от позиции
                if (pos.IsLong)
                    stopPrice = Math.Max(stopPrice, prevStopPrice);
                else if (pos.IsShort)
                    stopPrice = (stopPrice == 0)? prevStopPrice : (Math.Min(stopPrice, prevStopPrice));

                // если значение стопа было вычислено, то выставляем стоп на следующий бар
                if (pos.IsLong && stopPrice != 0)
                    pos.CloseAtStop(numBar + 1, stopPrice, Slippage * pos.Security.Tick, "LXS");
                else if (pos.IsShort && Math.Abs(stopPrice - double.MaxValue) > Eps)
                    pos.CloseAtStop(numBar + 1, stopPrice, Slippage * pos.Security.Tick, "SXS");
            }
        }

        /// <summary>
        /// Выставление тейк-профита для позиции
        /// </summary>
        /// <param name="pos">Позиция</param>
        /// <param name="numBar">Номер бара, на котором выполняется действие, тейк-профит выставляется на следующий бар</param>
        public void SetTakeProfit(IPosition pos, int numBar)
        {
            if (pos != null && pos.IsActiveForBar(numBar))
            {
                double profitPrice = pos.GetProfitPrice(TakeProfitPct);

                if (pos.IsLong)
                    pos.CloseAtProfit(numBar + 1, profitPrice, Slippage * pos.Security.Tick, "LXP");
                else
                    pos.CloseAtProfit(numBar + 1, profitPrice, Slippage * pos.Security.Tick, "SXP");
            }
        }
    }

    public enum RegimeBot
    {
        On,
        OnlyLong,
        OnlyShort,
        OnlyClosePosition
    }

    public enum StopType
    {
        Stop,
        Trail,
        Parabolic
    }
}
