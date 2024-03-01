using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace AzdoPackageScrape
{
    internal static class DataflowExtensions
    {
        // see: https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Threading.Tasks.Dataflow/src/Base/DataflowBlock.IAsyncEnumerable.cs
        public static IAsyncEnumerable<TOutput> ReceiveAllAsync<TOutput>(this IReceivableSourceBlock<TOutput> source, CancellationToken cancellationToken = default)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return Core(source, cancellationToken);

            static async IAsyncEnumerable<TOutput> Core(IReceivableSourceBlock<TOutput> source, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                while (await source.OutputAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (source.TryReceive(out TOutput item))
                    {
                        yield return item;
                    }
                }
            }
        }
    }
}
