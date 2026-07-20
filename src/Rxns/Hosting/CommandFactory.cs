using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Threading.Tasks;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns.Health
{
    public interface ICreateCommands
    {
        IObservable<RxnQuestion> ToCommands(IObservable<HttpResponseMessage> response);
    }

    public class CommandFactory : ICreateCommands
    {

        public IObservable<RxnQuestion> ToCommands(IObservable<HttpResponseMessage> response)
        {
            return Rxn.Create<RxnQuestion>(o =>
            {
                return response.Subscribe(msg =>
                {
                    msg.Content.ReadAsStringAsync().ToObservable().Subscribe(result =>
                    {
                        try
                        {
                            if (String.IsNullOrWhiteSpace(result)) return;

                            foreach (var cmd in ToCommands(result))
                                o.OnNext(cmd);
                        }
                        catch (Exception e)
                        {
                            o.OnError(e);
                        }
                        finally
                        {
                            o.OnCompleted();
                        }
                    },
                    error => o.OnError(error));

                }, error => o.OnError(error));
            });
        }

        public RxnQuestion[] ToCommands(string json)
        {
            //todo: fix why cmds are null on deserial
            return json.Deserialise<RxnQuestion[]>().Where(c => c != null).ToArray();
        }
    }
}
