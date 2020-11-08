using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TSLab.Script;
using TSLab.Script.Handlers;
using TSLab.Script.Optimization;

namespace TestScripts
{
    public class TestScripts : IExternalScript
    {
        public EnumOptimProperty Display = new EnumOptimProperty(DisplayGraph.Display);

        public void Execute(IContext ctx, ISecurity sec)
        {
            
            if (Display.Value.ToString().Equals(DisplayGraph.Display.ToString()))
            {
                IGraphPane pane = ctx.First ?? ctx.CreateGraphPane("First", "First");
                Color colorCandle = ScriptColors.Black;

                var graphSec = pane.AddList(sec.Symbol, sec, CandleStyles.BAR_CANDLE, colorCandle, PaneSides.RIGHT);
            }
            
        }

        public enum DisplayGraph
        {
            Display,
            NoDisplay
        }
    }
}