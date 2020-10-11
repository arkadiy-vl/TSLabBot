using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TSLab.DataSource;
using TSLab.Script;
using TSLab.Script.Handlers;

namespace TSLabBot
{
    public static class TradeHelper
    {
        #region Индикаторные хелперы
        /// <summary>
        /// Возвращает истину если на заданном баре первая кривая пересекает вторую вверх.
        /// </summary>
        /// <param name="list0">Первая кривая</param>
        /// <param name="list1">Вторая кривая</param>
        /// <param name="bar">Номер бара на котором искать пересечение</param>
        /// <returns></returns>
        public static bool CrossUp(this IList<double> list0, IList<double> list1, int bar)
        {
            if ((list0[bar - 1] <= list1[bar - 1]) && (list0[bar] > list1[bar]))
                return true;

            return false;
        }

        /// <summary>
        /// Возвращает истину если на заданном баре первая кривая пересекает вторую вниз.
        /// </summary>
        /// <param name="list0">Первая кривая</param>
        /// <param name="list1">Вторая кривая</param>
        /// <param name="bar">Номер бара на котором искать пересечение</param>
        /// <returns></returns>
        public static bool CrossDown(this IList<double> list0, IList<double> list1, int bar)
        {
            return CrossUp(list1, list0, bar);
        }

        /// <summary>
        /// Производит вычитание двух коллекций. 
        /// Из первой вычитает вторую и возвращает коллекцию с элементами равными разности элементов коллекций 1 и 2.
        /// Если колллекции разной длины, то вернет null.
        /// </summary>
        /// <param name="list">Коллекция из которой вычитать</param>
        /// <param name="subtrList">Колллекция которую будет вычитать.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Если списки разной длины.</exception>
        public static IList<double> Subtract(this IList<double> list, IList<double> subtrList)
        {
            // Если длины коллекций различаются то просто вернем null как знак ошибки.
            if (list.Count != subtrList.Count)
                throw new ArgumentException("Списки должны быть одинаковой длины");

            // Создаем массив, и забиваем его разностями элементов списков.
            var res = new double[list.Count];
            for (var i = 0; i < list.Count; i++)
                res[i] = list[i] - subtrList[i];


            return res;
        }

        /// <summary>
        /// Производит вычитание двух коллекций. 
        /// Из первой вычитает вторую и возвращает коллекцию с элементами равными разности элементов коллекций 1 и 2.
        /// Если колллекции разной длины, то вернет null.
        /// </summary>
        /// <param name="list">Коллекция из которой вычитать</param>
        /// <param name="subtrList">Колллекция которую будет вычитать.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Если списки разной длины.</exception>
        public static IList<int> Subtract(this IList<int> list, IList<int> subtrList)
        {
            // Если длины коллекций различаются то просто вернем null как знак ошибки.
            if (list.Count != subtrList.Count)
                throw new ArgumentException("Списки должны быть одинаковой длины");

            // Создаем массив, и забиваем его разностями элементов списков.
            var res = new int[list.Count];
            for (var i = 0; i < list.Count; i++)
                checked
                {
                    res[i] = list[i] - subtrList[i];
                }

            return res;
        }

        /// <summary>
        /// Производит сложение двух коллекций. 
        /// К первой коллекции прибавляет вторую и возвращает коллекцию с элементами равными сумме элементов коллекций 1 и 2.
        /// Если колллекции разной длины, то вернет null.
        /// </summary>
        /// <param name="list">Коллекция к которой прибавлять</param>
        /// <param name="subtrList">Колллекция которую будем прибавлять.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Если списки разной длины.</exception>
        public static IList<double> Add(this IList<double> list, IList<double> subtrList)
        {
            // Если длины коллекций различаются то просто вернем null как знак ошибки.
            if (list.Count != subtrList.Count)
                throw new ArgumentException("Списки должны быть одинаковой длины");

            // Создаем массив, и забиваем его суммами элементов списков.
            var res = new double[list.Count];
            for (var i = 0; i < list.Count; i++)
                res[i] = list[i] + subtrList[i];


            return res;
        }

        /// <summary>
        /// Производит сложение двух коллекций. 
        /// К первой коллекции прибавляет вторую и возвращает коллекцию с элементами равными сумме элементов коллекций 1 и 2.
        /// Если колллекции разной длины, то вернет null.
        /// </summary>
        /// <param name="list">Коллекция к которой прибавлять</param>
        /// <param name="subtrList">Колллекция которую будем прибавлять.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Если списки разной длины.</exception>
        public static IList<int> Add(this IList<int> list, IList<int> subtrList)
        {
            // Если длины коллекций различаются то просто вернем null как знак ошибки.
            if (list.Count != subtrList.Count)
                throw new ArgumentException("Списки должны быть одинаковой длины");

            // Создаем массив, и забиваем его суммами элементов списков.
            var res = new int[list.Count];
            for (var i = 0; i < list.Count; i++)
                checked
                {
                    res[i] = list[i] + subtrList[i];
                }

            return res;
        }

        /// <summary>
        /// Универсальный метод сложения. Складывает два списка разных объектов. Возвращает список сумм.
        /// </summary>
        /// <typeparam name="TSource">Тип исходного списка.</typeparam>
        /// <param name="list">Список к которому прибавлять</param>
        /// <param name="secList">Список который прибавлять</param>
        /// <param name="selector">Селектор</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Если списки разной длины.</exception>
        public static IList<double> Add<TSource>(this IList<TSource> list, IList<TSource> secList, Func<TSource, double> selector)
        {
            // Если длины коллекций различаются то просто вернем null как знак ошибки.
            if (list.Count != secList.Count)
                throw new ArgumentException("Списки должны быть одинаковой длины");

            var res = new double[list.Count];

            // Заполняем массив суммами, для выбора элемента который суммировать используем селектор.
            // Включаем контроль переполнения типа.
            for (var i = 0; i < list.Count; i++)
                res[i] = selector(list[i]) + selector(secList[i]);

            return res;
        }

        /// <summary>
        /// Универсальный метод вычитания. Вычитает два списка разных объектов. Возвращает список разностей.
        /// </summary>
        /// <typeparam name="TSource">Тип исходного списка.</typeparam>
        /// <param name="list">Список из которого вычитать</param>
        /// <param name="secList">Список который вычитать</param>
        /// <param name="selector">Селектор</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Если списки разной длины.</exception>
        public static IList<double> Subtract<TSource>(this IList<TSource> list, IList<TSource> secList, Func<TSource, double> selector)
        {
            // Если длины коллекций различаются то просто вернем null как знак ошибки.
            if (list.Count != secList.Count)
                throw new ArgumentException("Списки должны быть одинаковой длины");

            var res = new double[list.Count];

            // Заполняем массив разностями, для выбора элемента который вычитать используем селектор.
            // Включаем контроль переполнения типа.
            for (var i = 0; i < list.Count; i++)
                res[i] = selector(list[i]) - selector(secList[i]);

            return res;
        }
        #endregion

        #region Общие хелперы
        public static bool IsNull(this object obj)
        {
            return obj == null;
        }

        /// <summary>
        /// Метод упрощающий форматирование строк.
        /// </summary>
        /// <param name="str">Строка для форматирования.</param>
        /// <param name="args">Аргументы для форматирования.</param>
        /// <returns></returns>
        public static string Put(this string str, params object[] args)
        {
            return string.Format(str, args);
        }

        /// <summary>
        /// Сравнивает два значения double. Нужен для сравнения цен. Использует дельту 1Е-10
        /// Если разница между двумя значениями меньше дельты вернет истину.
        /// </summary>
        /// <param name="d1"></param>
        /// <param name="d2"></param>
        /// <returns></returns>
        public static bool IsPriceEqual(this double d1, double d2)
        {
            return Math.Abs(d1 - d2) < 1E-10;
        }
        #endregion

        #region Хелперы позиций
        /// <summary>
        /// Если сделка была закрыта и открыта между клирингами она считается скальперской на РТС.
        /// Учитывается дневной и вечерний клиринг.
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public static bool IsScalp(this IPosition pos)
        {
            if (pos.IsActive)
                throw new ArgumentException("Нельзя определить скальперская сделка или нет для НЕЗАКРЫТОЙ позиции.");

            // НА ФОРТС если удержание было в течение одной сесси до клиринга, комиссия берется в два раза меньше.
            // Учитываем число лотов и ВХОД + ВЫХОД. Комис применяется и туда и сюда. По факту наш комисс будет прописан в выходе из позиции
            var dayClearingTime = new TimeSpan(14, 01, 00);
            var eveningClearingTime = new TimeSpan(18, 50, 00);

            var entryDate = pos.EntryBar.Date;
            var exitDate = pos.ExitBar.Date;

            // Если сделка была открыта и закрыта в между дневным и вечерним клирингом
            var isScalp = (entryDate.Date == exitDate.Date)
                             && (entryDate.TimeOfDay.InRange(dayClearingTime, eveningClearingTime)
                             && (exitDate.TimeOfDay.InRange(dayClearingTime, eveningClearingTime)))

                          // Если сделка была открыта и закрыта в между вечерним и дневным клирингом следующего дня
                          // Если не попала сделка между дневным и вечерним клиром, тогда она попала между вечерним и дневным.
                          || (entryDate.Date == exitDate.Date - TimeSpan.FromDays(1))
                             && (entryDate.TimeOfDay.InRange(dayClearingTime, eveningClearingTime) == false
                               && (exitDate.TimeOfDay.InRange(dayClearingTime, eveningClearingTime) == false));

            return isScalp;
        }

        /// <summary>
        /// Возвращает среднюю цену входа для всех позиций из списка.
        /// </summary>
        /// <param name="positions">Спислк позиций для которых расчитать</param>
        /// <returns></returns>
        public static double AvgEntryPrice(this IList<IPosition> positions)
        {
            var totalPrice = positions.Sum(p => p.PositionEntryPrice());
            var totalSize = positions.Sum(p => p.PosSize());

            return totalPrice / totalSize;
        }

        /// <summary>
        /// Полный размер позиции в бумагах. Учитывается размер лота.
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public static double PosSize(this IPosition pos)
        {
            return pos.Shares * pos.Security.LotSize;
        }

        /// <summary>
        /// Возвращает общую стоимость позиции на момент входа
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public static double PositionEntryPrice(this IPosition pos)
        {
            return pos.EntryPrice * pos.PosSize();
        }

        /// <summary>
        /// Получить цену выхода из позиции с учетом проскальзывания в шагах цены
        /// </summary>
        /// <param name="pos">Позиция, для которой требуется получить цену выхода</param>
        /// <param name="exitPrice">Цена выхода без проскальзывания</param>
        /// <param name="slippage">Проскальзывание в шагах цены</param>
        /// <returns></returns>
        public static double GetExitPrice(this IPosition pos, double exitPrice, int slippage)
        {
            if (pos.IsLong)
                return exitPrice - slippage * pos.Security.Tick;
            else if (pos.IsShort)
                return exitPrice + slippage * pos.Security.Tick;
            else
                throw new ArgumentException($"Не определенное направление позиции {pos}");
        }

        /// <summary>
        /// Открыть новую короткую позицию по указанной цене с учетом проскальзывания в шагах цены.
        /// </summary>
        /// <param name="sec">Инструмент</param>
        /// <param name="barNum">Номер бара</param>
        /// <param name="shares">Количество лотов</param>
        /// <param name="price">Цена</param>
        /// <param name="slippage">Проскальзывание в шагах цены</param>
        /// <param name="signalName">Название сигнала входа в позицию</param>
        /// <param name="notes">Дополнительное описание к сигналу</param>
        public static void SellAtPriceSlip(this ISecurity sec, int barNum, double shares, double price, int slippage, string signalName, string notes = null)
        {
            double enterPrice = price - slippage * sec.Tick;
            sec.Positions.SellAtPrice(barNum, shares, enterPrice, signalName, notes);
        }

        /// <summary>
        /// Открыть новую длинную позицию по указанной цене с учетом проскальзывания в шагах цены.
        /// </summary>
        /// <param name="sec">Инструмент</param>
        /// <param name="barNum">Номер бара</param>
        /// <param name="shares">Количество лотов</param>
        /// <param name="price">Цена</param>
        /// <param name="slippage">Проскальзывание в шагах цены</param>
        /// <param name="signalName">Название сигнала входа в позицию</param>
        /// <param name="notes">Дополнительное описание к сигналу</param>
        public static void BuyAtPriceSlip(this ISecurity sec, int barNum, double shares, double price, int slippage, string signalName, string notes = null)
        {
            double enterPrice = price + slippage * sec.Tick;
            sec.Positions.BuyAtPrice(barNum, shares, enterPrice, signalName, notes);
        }

        /// <summary>
        /// Закрыть позицию по указанной цене с учетом проскальзывания в шагах цены
        /// </summary>
        /// <param name="pos">Позиция</param>
        /// <param name="barNum">Номер бара</param>
        /// <param name="price">Цена закрытия</param>
        /// <param name="slippage">Проскальзывание в шагах цены</param>
        /// <param name="signalName">Название сигнала выхода из позиции</param>
        /// <param name="notes">Дополнительное описание к сигналу</param>
        public static void CloseAtPriceSlip(this IPosition pos, int barNum, double price, int slippage, string signalName, string notes = null)
        {
            double exitPrice = 0;
            if (pos.IsLong)
                exitPrice = price - slippage * pos.Security.Tick;
            else if (pos.IsShort)
                exitPrice = price + slippage * pos.Security.Tick;
            else
                throw new ArgumentException($"Не определенное направление позиции {pos}");

            pos.CloseAtPrice(barNum, exitPrice, signalName, notes);
        }

        /// <summary>
        /// Получить цену стоп-лоса из процента стоп-лоса 
        /// </summary>
        /// <param name="pos">Позиция</param>
        /// <param name="percentStop">Процент стоп-лоса</param>
        /// <returns>Цена стоп-лоса</returns>
        public static double GetStopPrice(this IPosition pos, int percentStop)
        {
            if (pos.IsLong)
                return pos.EntryPrice * (1 - percentStop / 100.0);
            else if (pos.IsShort)
                return pos.EntryPrice * (1 + percentStop / 100.0);
            else
                throw new ArgumentException($"Не определенное направление позиции {pos}");
        }

        /// <summary>
        /// Получить цену тейк-профита для позиции из процента тейк-профита
        /// </summary>
        /// <param name="pos">Позиция</param>
        /// <param name="percentProfit">Процент тейк-профита</param>
        /// <returns>Цена тейк-профита</returns>
        public static double GetProfitPrice(this IPosition pos, int percentProfit)
        {
            if (pos.IsLong)
                return pos.EntryPrice * (1 + percentProfit / 100.0);
            else if (pos.IsShort)
                return pos.EntryPrice * (1 - percentProfit / 100.0);
            else
                throw new ArgumentException($"Не определенное направление позиции {pos}");
        }
        #endregion

        #region Хелперы времени/даты
        /// <summary>
        /// Возвращает истину если время лежит в заданных границах, ВКЛЮЧИТЕЛЬНО!
        /// </summary>
        /// <param name="time">Время</param>
        /// <param name="minTime">Минимальная граница</param>
        /// <param name="maxTime">Максимальная граница</param>
        /// <returns></returns>
        public static bool InRange(this TimeSpan time, TimeSpan minTime, TimeSpan maxTime)
        {
            return time >= minTime && time <= maxTime;
        }
        #endregion
    }
}
