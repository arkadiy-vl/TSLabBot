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

namespace TSLabBot
{
    /// <summary>
    /// Трендовый бот по двум скользящим средним
    /// с возможностью добавления позиции при достижении заданного профита
    /// с возможность выхода по стоплосу и тейкпрофиту
    /// </summary>
    public class Bot2SMA : IExternalScript
    {

        # region === Параметры робота ===
        public IntOptimProperty PeriodFastMA = new IntOptimProperty(50, 10, 100, 5);
        public IntOptimProperty PeriodSlowMA = new IntOptimProperty(100, 20, 200, 10);
        public IntOptimProperty SizeStopLoss = new IntOptimProperty(5, 1, 10, 1);
        public IntOptimProperty SizeTakeProfit = new IntOptimProperty(5, 1, 10, 1);
        public BoolOptimProperty OnStopLoss = new BoolOptimProperty(true);
        public BoolOptimProperty OnTakeProfit = new BoolOptimProperty(true);
        public BoolOptimProperty OnAddPos = new BoolOptimProperty(true);
        public IntOptimProperty SizeProfitAddPos = new IntOptimProperty(2, 2, 10 ,2);
        public OptimProperty CommissionPct = new OptimProperty(0.1, 0.05, 0.2, 0.01 );

        // не понятно как сделать перечисление оптимизируемым параметром 
        //public EnumOptimProperty Regim = new EnumOptimProperty(RegimeBot.On, false);

        // режим работы бота
        RegimeBot _regimeBot = RegimeBot.On;
        #endregion

        public void Execute(IContext ctx, ISecurity sec)
        {
            var sw = Stopwatch.StartNew();

            if (PeriodFastMA.Value >= PeriodSlowMA.Value)
                return;

            // расчет индикаторов c кэшированием
            IList<double> fastMA = ctx.GetData("MA", new string[] { PeriodFastMA.ToString() },
                () => Series.SMA(sec.ClosePrices, PeriodFastMA));

            IList<double> slowMA = ctx.GetData("MA", new string[] { PeriodSlowMA.ToString() },
                () => Series.SMA(sec.ClosePrices, PeriodSlowMA));

            // последний сформировавшийся бар для расчетов
            var barsCount = sec.Bars.Count;
            if (!ctx.IsLastBarUsed)
            {
                barsCount--;
            }
            //ctx.Log($"IsLastBarUsed - {ctx.IsLastBarUsed}", MessageType.Info, true);

            // первый используемый для расчетов бар
            int startBar = Math.Max(Math.Max(PeriodFastMA + 1, PeriodSlowMA + 1), ctx.TradeFromBar);

            // цены закрытия
            var closePrices = sec.ClosePrices;

            // масивы для дополнительного вывода на график сигналов на вход в позицию
            var arrSignalLE = new double[barsCount];
            var arrSignalSE = new double[barsCount];

            // кубик доход
            var profitPctHandler = new TSLab.Script.Handlers.ProfitPct(){};

            // комиссия
            var relCommisionHandler = new TSLab.Script.Handlers.RelativeCommission();
            relCommisionHandler.CommissionPct = CommissionPct.Value;
            relCommisionHandler.Execute(sec);

            //--------------
            // Торговый цикл
            //--------------
            for (int i = startBar; i < barsCount; i++)
            {
                if (_regimeBot == RegimeBot.Off)
                    break;

                if (PeriodFastMA >= PeriodSlowMA)
                    break;

                // последняя цена инструмента
                var lastPrice = closePrices[i];

                // вычисляем торговые сигналы
                var signalLE = fastMA[i] > slowMA[i] &&
                               fastMA[i - 1] <= slowMA[i - 1];

                var signalSE = fastMA[i] < slowMA[i] &&
                               fastMA[i - 1] >= slowMA[i - 1];

                // заполняем массивы сигналов на вход в позицию
                arrSignalLE[i] = signalLE ? 1: 0;     
                arrSignalSE[i] = signalSE ? 1: 0;     

                // получаем активные позиции
                var longPosition = sec.Positions.GetLastActiveForSignal("LE", i);
                var shortPosition = sec.Positions.GetLastActiveForSignal("SE", i);
                var longPositionAdd = sec.Positions.GetLastActiveForSignal("LA", i);
                var shortPositionAdd = sec.Positions.GetLastActiveForSignal("SA", i);

                // условия входа в лонг
                if (longPosition == null)
                {
                    if (signalLE && _regimeBot != RegimeBot.OnlyShort && _regimeBot != RegimeBot.OnlyClosePosition)
                    {
                        sec.Positions.BuyAtMarket(i + 1, 1, "LE");
                    }
                }
                // условия выхода из лонга
                else
                {
                    // получаем профит по позиции
                    var profitPct = profitPctHandler.Execute(longPosition, i);
                    
                    // если профит более определенной величины, то добавляем позицию
                    if(OnAddPos && profitPct > SizeProfitAddPos && longPosition.Shares <= 2)
                    {
                        longPosition.ChangeAtMarket(i + 1, longPosition.Shares + 1, "LA");
                    }

                    if (signalSE)
                    {
                        longPosition.CloseAtMarket(i + 1, "LX");
                    }
                }
                // условия входа в шорт
                if (shortPosition == null)
                {
                    if (signalSE && _regimeBot != RegimeBot.OnlyLong && _regimeBot != RegimeBot.OnlyClosePosition)
                    {
                        sec.Positions.SellAtMarket(i + 1, 1, "SE");
                    }
                }
                // условия выхода из шорта
                else
                {
                    // получаем профит по позиции
                    var profitPct = profitPctHandler.Execute(shortPosition, i);

                    // если профит более определенной величины, то добавляем позицию до двух контрактов
                    if (OnAddPos && profitPct > SizeProfitAddPos && shortPosition.Shares < 2)
                    {
                        shortPosition.ChangeAtMarket(i + 1, -1 * shortPosition.Shares - 1, "SA");
                    }

                    if (signalLE)
                    {
                        shortPosition.CloseAtMarket(i + 1, "SX");
                    }
                }

                // выставление стоп лосса
                if(OnStopLoss.Value)
                {
                    longPosition?.CloseAtStop(i + 1, longPosition.EntryPrice * (1 - SizeStopLoss/100.0), "LXS");
                    shortPosition?.CloseAtStop(i + 1, shortPosition.EntryPrice * (1 + SizeStopLoss/100.0), "SXS");
                }

                // выставление тейк профита
                if(OnTakeProfit.Value)
                {
                    longPosition?.CloseAtProfit(i + 1, longPosition.EntryPrice * (1 + SizeTakeProfit/100.0), "LXP");
                    shortPosition?.CloseAtProfit(i + 1, shortPosition.EntryPrice * (1 - SizeTakeProfit/100.0), "SXP");
                }
                
            }

            // Если идет процесс оптимизации, то графики рисовать не нужно, это замедляет работу
            if (ctx.IsOptimization)
            {
                return;
            }

            #region ===Прорисовка графиков===
            //--------------------
            IGraphPane pane = ctx.First ?? ctx.CreateGraphPane("First", "First");
            Color colorCandle = ScriptColors.Black;
            
            var graphSec = pane.AddList(sec.Symbol, 
                sec, 
                CandleStyles.BAR_CANDLE,
                colorCandle,
                PaneSides.RIGHT);
            
            var graphFastSma = pane.AddList(String.Format("FastMA ({0})", PeriodFastMA),
                fastMA,
                ListStyles.LINE,
                ScriptColors.Red,
                LineStyles.SOLID,
                PaneSides.RIGHT);
            
            var graphSlowSma = pane.AddList(String.Format("SlowMA ({0})", PeriodSlowMA),
                slowMA,
                ListStyles.LINE,
                ScriptColors.Blue,
                LineStyles.SOLID,
                PaneSides.RIGHT);

            var graphSignalLE = pane.AddList("Signal LE",
                arrSignalLE,
                ListStyles.HISTOHRAM,
                ScriptColors.Green,
                LineStyles.SOLID,
                PaneSides.LEFT);

            var graphignalSE = pane.AddList("Signal SE",
                arrSignalSE,
                ListStyles.HISTOHRAM,
                ScriptColors.Red,
                LineStyles.SOLID,
                PaneSides.LEFT);

            // раскрашиваем бары в зависимости от наличия позиции
            for (int i = 0; i < barsCount; i++)
            {
                var activePositions = sec.Positions.GetActiveForBar(i);
                graphSec.SetColor(i, activePositions.Any() ? ScriptColors.Black : ScriptColors.DarkGray);
            }
            #endregion

           

        }
        public enum RegimeBot
        {
            On,
            Off,
            OnlyLong,
            OnlyShort,
            OnlyClosePosition
        }

    }

   
}
