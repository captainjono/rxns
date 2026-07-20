using System;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Rxns.DDD.Commanding;

namespace Rxns.Hosting
{
    //todo: remove this interface
    public interface ICreateEvents
    {
        IObservable<IRxnQuestion[]> ToCommands(IObservable<HttpResponseMessage> response);
    }

    public class EventFactory : ICreateEvents
    {

        public IObservable<IRxnQuestion[]> ToCommands(IObservable<HttpResponseMessage> response)
        {
            return Rxn.Create<IRxnQuestion[]>(o =>
            {
                return response.Subscribe(msg =>
                {
                    msg.Content.ReadAsStringAsync().ToObservable().Subscribe(result =>
                        {
                            try
                            {
                                if (result.IsNullOrWhitespace() || result == "[]") return;

                                var r = ToCommands(result);
                                o.OnNext(r);
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
            }).ToArray().SelectMany(r => r);
        }

        public RxnQuestion[] ToCommands(string json)
        {
            if (json == "[]") return new RxnQuestion[0];
            //todo: fix why cmds are null on deserial
            return json.Deserialise<RxnQuestion[]>().Where(c => c != null).ToArray();
        }
    }
}
