using System;

namespace Rxns.Commanding
{
    /// <summary>
    /// A result of a paging operation
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class PagingResult<T>
    {
        public long Total;
        public T[] Records;

        public PagingResult(T[] records, long total)
        {
            Records = records;
            Total = total;
        }
    }

    /// <summary>
    /// This class is used to generate a continuation token for a list that you want
    /// to download in sections
    /// Index = 1 = first page
    /// - Null or 0 values here indicate return all records, bypassing paging
    /// </summary>
    public class PagingToken
    {
        /// <summary>
        /// The page size
        /// </summary>
        public readonly int? Size;

        /// <summary>
        /// The total record cound
        /// </summary>
        public readonly long? Total;

        /// <summary>
        /// The current index
        /// </summary>
        public readonly int? Index;

        public override string ToString()
        {
            return "{0}_{1}_{2}".FormatWith(Size, Total, Index);//.AsBase64();
        }

        public PagingToken(int? maxRecords = null)
        {
            Size = maxRecords;
            Index = maxRecords.HasValue ? 1 : (int?)null;
        }

        public PagingToken(int? maxRecords, int? index, long? total)
        {
            Size = maxRecords;
            Index = index;
            Total = total;
        }

        public PagingToken Next(int recordCount, long total)
        {
            return new PagingToken(Size, Index + 1, total);
        }

        public static Continuation<T[]> Next<T>(ContinuationToken input, Func<int?, int?, PagingResult<T>> doPaging)
        {
            var existing = input ?? new ContinuationToken();
            var paging = FromToken(existing.Token, existing.Size);

            var result = doPaging(paging.Index, paging.Size);

            var next = paging.Next(result.Records.Length, result.Total);
            return new Continuation<T[]>(result.Records, existing.Next(next.ToString(), result.Total, next.HasMorePages()));
        }

        public bool HasMorePages()
        {
            return (Size == null || Size == 0) ? false : (Index * Size) < (Total + Size);
        }

        public static PagingToken FromToken(string token = null, int? size = null)
        {
            if (token.IsNullOrWhitespace()) return new PagingToken(size);
            var parts = token.Split('_'); //.FromBase64AsString()

            return new PagingToken(parts[0].AsNullableInt(), parts.Length > 1 ? parts[2].AsNullableInt() : null, parts[1].AsNullableInt());
        }
    }
}
