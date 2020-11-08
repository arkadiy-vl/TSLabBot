using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TSLab.Script.Handlers;

namespace TwoLegArbitrage
{
    public static class TradeHelper
    {
        public static IList<double> Subtract(this IList<double> list, IList<double> subtrList)
        {
            if (subtrList == null)
                throw new ArgumentNullException(nameof(subtrList));

            if (list.Count != subtrList.Count)
                return null;

            var result = new double[list.Count];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = list[i] - subtrList[i];
            }

            return result;
        }

        public static IList<double> Add(this IList<double> list, IList<double> addList)
        {
            if (addList == null)
                throw new ArgumentNullException(nameof(addList));

            if (list.Count != addList.Count)
                return null;

            var result = new double[list.Count];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = list[i] + addList[i];
            }

            return result;
        }

        public static IList<double> MultiplyConst(this IList<double> list, double k)
        {
            var result = new double[list.Count];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = list[i] * k;
            }

            return result;
        }

        public static IList<double> Divide(this IList<double> list, IList<double> divList)
        {
            if (divList == null)
            {
                throw new ArgumentNullException(nameof(divList));
            }
            
            if (list.Count != divList.Count)
                return null;

            var result = new double[list.Count];

            for (int i = 0; i < result.Length; i++)
            {
                if (divList[i] == 0)
                {
                    throw new ArgumentNullException(nameof(divList));
                }
                else
                {
                    result[i] = list[i] / divList[i];
                }
            }

            return result;
        }

        public static void  LogInfo(this IContext ctx, string msg)
        {
            ctx.Log(msg, MessageType.Info, true);
        }

        public static void LogInfo(this IContext ctx, string msg, params object[] args)
        {
            var message = string.Format(msg, args);
            ctx.Log(message, MessageType.Info, true);
        }

        public static void LogError(this IContext ctx, string msg)
        {
            ctx.Log(msg, MessageType.Error, true);
        }

        /// <summary>
        /// Вычисление центра канала
        /// </summary>
        /// <param name="upChannel">верхняя граница канала</param>
        /// <param name="downChannel">нижняя граница канала</param>
        /// <returns></returns>
        public static IList<double> CenterChannel(IList<double> upChannel, IList<double> downChannel)
        {

            if (upChannel == null || downChannel == null)
            {
                return null;
            }

            if (upChannel.Count != downChannel.Count)
            {
                return null;
            }

            var count = upChannel.Count;
            var centerChannel = new double[count];

            for (int i = 0; i < count; ++i)
            {
                centerChannel[i] = downChannel[i] + (upChannel[i] - downChannel[i]) / 2.0;
            }

            return (IList<double>)centerChannel;
        }

    }
}
