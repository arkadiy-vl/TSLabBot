using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TSLab.Script.Handlers;

namespace TSLabBot
{
    public static class TradeHelper
    {
        public static IList<double> Subtract(this IList<double> list, IList<double> subtrList)
        {
            if (list.Count != subtrList.Count)
                return null;

            var res = new Double[list.Count];

            for (int i = 0; i < res.Length; i++)
            {
                res[i] = list[i] - subtrList[i];
            }

            return res;
        }

        public static void  LogInfo(this IContext ctx, string msg)
        {
            ctx.Log(msg, MessageType.Info, true);
        }

    }
}
