using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Helpers;
using TSLab.Script.Optimization;

namespace BotDonchianPosAdd
{
    // Скрипт может входить только в лонг.
    // По пробою канала дончиана на базовом таймфрейме, входим в лонг.
    // Дальше, если закрепились выше канала по дневному таймфрейму, наращиваем позу двойным размером от текущей активной позиции.
    // Если мы еще не нарастились по дневному каналу, то ставим отложенную заявку на добавление по достижения нужного профита.
    // Изначально ставим стоп и тейк. По достижению профита тащим стоп в безубыток.
    // Если же мы взяли позу по пробою дневного канала, тогда дальше танем общую позицию трейлом. Без тейков.

    public class BotDonchianPosAdd : IExternalScript
    {
        #region параметры бота
        // режим работы бота
        RegimeBot regim = RegimeBot.On;

        // период канала для основного таймфрейма
        public IntOptimProperty PeriodChannel = new IntOptimProperty(50, 10, 100, 5);
        
        // период канала для дневного таймфрейма
        public IntOptimProperty PeriodChannelDay = new IntOptimProperty(50, 10, 100, 5);

        // уровень в процентах, с которого надо добавлять позицию
        public OptimProperty AddPositionLevel = new OptimProperty(1, 0.2, 5, 0.2);

        // проскальзывание в шагах цены
        public IntOptimProperty Slippage = new IntOptimProperty(20, 1, 500, 1);

        // включить стоп лосс
        public BoolOptimProperty OnStopLoss = new BoolOptimProperty(false);

        // размер стоп-лоса в процентах
        public IntOptimProperty StopLossPct = new IntOptimProperty(3, 1, 10, 1);

        // включить тейк профит
        public BoolOptimProperty OnTakeProfit = new BoolOptimProperty(false);

        // размер тейк-профита в процентах
        public IntOptimProperty TakeProfitPct = new IntOptimProperty(5, 1, 10, 1);

        // включить перевод в безубыток
        public BoolOptimProperty OnBreakevenStop = new BoolOptimProperty(false);

        // размер профита для перевода позиции в безубыток
        public IntOptimProperty ProfitForBreakevenPct = new IntOptimProperty(3, 1, 10, 1);

        // уровень включить трейл-стоп в процентах
        public IntOptimProperty TrailStopEnablePct = new IntOptimProperty(5, 1, 10, 1);

        // размер трейл-стопа
        public IntOptimProperty TrailStopPct = new IntOptimProperty(5, 1, 10, 1);
        #endregion

        public void Execute(IContext ctx, ISecurity sec)
        {
            // таймер для определения времени выполнения скрипта
            var sw = Stopwatch.StartNew();

            // проверка таймфрейма, используемого для торговли
            if(sec.IntervalInstance != new Interval(30, DataIntervals.MINUTE))
                throw new InvalidOperationException($"Выбран не корректный интервал для торговли{sec.IntervalInstance}. Работать только на таймфрейме 30 минут!");

            #region расчет индикаторов
            // канал Дончиана для основного таймфрема
            IList<double> upChannel = ctx.GetData("UpChannel",
                new string[] { PeriodChannel.ToString() },
                () => Series.Highest(sec.HighPrices, PeriodChannel));
            IList<double> downChannel = ctx.GetData("DownChannel",
                new string[] { PeriodChannel.ToString() },
                () => Series.Lowest(sec.LowPrices, PeriodChannel));

            // свечи дневного таймфрейма
            var daySec = sec.CompressTo(new Interval(1, DataIntervals.DAYS));

            // канал Дончиана для дневного таймфрейма
            IList<double> upChannelDay = ctx.GetData("UpChannelDay",
                new string[] {PeriodChannelDay.ToString()},
                () =>
                {
                    var res = Series.Highest(daySec.HighPrices, PeriodChannelDay);
                    return daySec.Decompress(res);
                });

            IList<double> downChannelDay = ctx.GetData("DownChannelDay",
                new string[] { PeriodChannelDay.ToString() },
                () =>
                {
                    var res = Series.Lowest(daySec.LowPrices, PeriodChannelDay);
                    return daySec.Decompress(res);
                });
            #endregion

            #region первый и последний бар для расчетов
            // первый бар, используемый для расчетов
            int startBar = Math.Max(PeriodChannel + 1, ctx.TradeFromBar);

            // значение счетчика баров, используемое для расчетов
            int barsCount = ctx.BarsCount;
            if (!ctx.IsLastBarClosed)
                barsCount--;
            #endregion

            // цены закрытия, используемые для торговли
            var closePrices = sec.ClosePrices;

            // вначале трейл-стоп отключен, он будет включен при открытии добавочной позиции LED
            var onTrailStop = false;
            var trailEnable = false;
            var lastTrailPrice = 0.0;

            #region Торговый цикл
            for (int i = startBar; i < barsCount; i++)
            {
                // базовая позиция LEB
                var lebPosition = sec.Positions.GetLastActiveForSignal("LEB", i);

                // добавочная позиция LEA, открывается при получении заданного уровня профита по базовой позиции
                var leaPosition = sec.Positions.GetLastActiveForSignal("LEA", i);

                // суммарно базовая и добавочная позиции - позиции, начинающиеся на "LA"
                var lePositions = sec.Positions.GetActiveForBar(i).Where(p => p.EntrySignalName.StartsWith("LE")).ToList();

                // добавочная позиция LED при выходе цены за дневной канал
                var ledPosition = sec.Positions.GetLastActiveForSignal("LED", i);

                // если нет базовой позиции, то выставляем условную заявку на её открытие
                if (lebPosition == null)
                {
                    sec.Positions.BuyIfGreater(i + 1, 1, upChannel[i], Slippage * sec.Tick, "LEB");
                    onTrailStop = false;
                    trailEnable = false;
                    lastTrailPrice = 0.0;
                    OnStopLoss.Value = true;
                }
                else
                {
                    // если есть базовая позиция LEB, нет добавочной позиции LEA и нет добавочной позиции LED
                    // то выставляем условную заявку на открытие добавочной позиции LEA
                    if (leaPosition == null && ledPosition == null)
                    {
                        // цена входа в добавочную позицию
                        double entryPrice = lebPosition.EntryPrice * (1 + AddPositionLevel / 100.0);
                        sec.Positions.BuyIfGreater(i + 1, 1, entryPrice, Slippage, "LEA");
                    }

                    // если нет добавочной позиции LED и цена пробила дневной канал, то открываем добавочную позицию LED
                    if (ledPosition == null && (closePrices[i - 1] <= upChannelDay[i-1]) && (closePrices[i] > upChannelDay[i]))
                    {
                        sec.Positions.BuyAtMarket(i+1, 2, "LED");
                        OnStopLoss.Value = false;
                        onTrailStop = true;
                    }

                    // выставляем стоп-лосс и тейк-профит для всех открытых позиций по средней цене входа,
                    // вычисляем среднюю цену входа в позицию
                    var avrEntryPrice = lePositions.GetAvgEntryPrice();

                    // стоп-лосc
                    foreach(var pos in lePositions)
                    {
                        if (OnStopLoss || OnBreakevenStop || onTrailStop)
                        {
                            // цена стоп-лосса
                            double stopPrice = (OnStopLoss)
                                ? (avrEntryPrice * (1 - StopLossPct / 100.0))
                                : 0;

                            // цена инструмента для перевода позиции в безубыток
                            double movePosBreakevenPrice = avrEntryPrice * (1 + ProfitForBreakevenPct / 100.0);

                            // цена стопа для перевода позиции в безубыток
                            double breakevenStopPrice = (OnBreakevenStop && closePrices[i] > movePosBreakevenPrice)
                                ? avrEntryPrice
                                : 0;

                            // цена трейл-стопа
                            double trailPrice = 0.0;
                            if (onTrailStop)
                            {
                                double onTrailStopPrice = avrEntryPrice * (1 + TrailStopEnablePct / 100.0);
                                if(closePrices[i] > onTrailStopPrice)
                                    trailEnable = true;

                                trailPrice = (trailEnable)
                                    ? sec.HighPrices[i] * (1 - TrailStopPct / 100.0)
                                    : avrEntryPrice * (1 - StopLossPct / 100.0);

                                trailPrice = lastTrailPrice = Math.Max(trailPrice, lastTrailPrice);
                            }

                            // берем максимальное значение стопа-лосса
                            stopPrice = Math.Max(stopPrice, breakevenStopPrice);
                            stopPrice = Math.Max(stopPrice, trailPrice);
                            stopPrice = Math.Max(stopPrice, pos.GetStop(i));
                            if(stopPrice != 0)
                                pos.CloseAtStop(i + 1, stopPrice, Slippage, "LXS");
                        }
                    }

                    // тейк-профит
                    if (OnTakeProfit)
                    {
                        double profitPrice = avrEntryPrice * (1 + TakeProfitPct / 100.0);
                        lePositions.ForEach(p => p.CloseAtProfit(i + 1, profitPrice, Slippage, "LXP"));
                    }
                }

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
            pane.AddList(sec.Symbol,
                sec,
                CandleStyles.BAR_CANDLE,
                colorCandle,
                PaneSides.RIGHT);

            var lineUpChannel = pane.AddList(string.Format($"UpChannel ({PeriodChannel,0})"),
                upChannel,
                ListStyles.LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var lineDownChannel = pane.AddList(string.Format($"DwChannel ({PeriodChannel,0}"),
                downChannel,
                ListStyles.LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var lineUpChannelDay = pane.AddList(string.Format($"UpChannelDay ({PeriodChannelDay,0}"),
                upChannelDay,
                ListStyles.LINE,
                ScriptColors.Red,
                LineStyles.DASH,
                PaneSides.RIGHT);
            lineUpChannelDay.Thickness = 2;

            var lineDownChannelDay = pane.AddList(string.Format($"DwChannelDay ({PeriodChannelDay,0}"),
                downChannelDay,
                ListStyles.LINE,
                ScriptColors.Red,
                LineStyles.DASH,
                PaneSides.RIGHT);
            lineDownChannelDay.Thickness = 2;

            #endregion

            #region'Вывод в лог'
            // Вывод в лог времени выполнения скрипта только в режиме Лаборатория
            if (!ctx.Runtime.IsAgentMode)
                ctx.Log($"Скрипт выполнен за время: {sw.Elapsed}", MessageType.Info, true);
            #endregion
        }
    }

    internal enum RegimeBot
    {
        On,
        OnlyLong,
        OnlyShort,
        OnlyClosePosition
    }
}
