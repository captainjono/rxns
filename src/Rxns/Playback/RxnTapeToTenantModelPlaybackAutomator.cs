using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rxns;
using Rxns.DDD.BoundedContext;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Playback;

namespace Janison.Micro
{


    public class RxnToTenantModelPlaybackAutomator : ReportsStatus, IRxnTapeToTenantRepoPlaybackAutomator
    {
        public void Play(string tapeDir, Func<string, IAggRoot> getById, Action<IAggRoot, IEnumerable<IDomainEvent>> save, ITapeRepository tapes, IFileSystemService fs, Action<ITapeStuff, ITapeRepository, Exception> onError = null)
        {
            var kCounter = 0;
            var repoCount = 0;
            Parallel.ForEach(tapes.GetAll(tapeDir), new ParallelOptions() { MaxDegreeOfParallelism = 2 }, tape =>
            {
                IDomainEvent lastEvent = null;

                try
                {
                    if (++repoCount == 1000)
                    {
                        kCounter++;
                        OnVerbose("[{0}] Saved {1}k repos".FormatWith(tapeDir, kCounter));
                        repoCount = 0;
                    }

                    var agg = getById(tape.Name);

                    //Debug.WriteLine("[PLAY] lookup up events from file");
                    var uncommited = tape.Source.Contents.Select(e => (IDomainEvent)e.Recorded);

                    //Debug.WriteLine("[PLAY] commiting events");
                    save(agg, uncommited.ToEnumerable().Select(@event =>
                    {
                        lastEvent = @event;
                        agg.ApplyChange(@event); //verify change before publishing
                        return @event;
                    }));
                }
                catch (Exception e)
                {
                    try
                    {
                        lastEvent = lastEvent ?? new DomainEvent("No events generated yet");
                        var exception = new PlaybackException("{0}({1}[{2}])".FormatWith(tape.Name, lastEvent.GetType().Name, lastEvent.Serialise()), e);
                        if (onError != null) onError(tape, tapes, exception);
                        else Debug.WriteLine("{0}".FormatWith(exception));
                    }
                    catch (Exception ee)
                    {
                        OnError(ee, "Error in error handler while processing {0}", tape.Name);
                    }
                }
                finally
                {
                    try
                    {
                        tapes.Delete(tape.Name);
                        //for some reason windows doesnt garentee to delete a file instantly
                        //so we need to wait for it to happen before moving on otherwise we get access violations.
                        while (fs.ExistsFile(Path.Combine(tapeDir, tape.Name)))
                        {
                            Thread.Sleep(1000);
                        }
                    }
                    catch (Exception ee)
                    {
                        OnError(ee);
                    }
                }
            });
        }
    }
}
