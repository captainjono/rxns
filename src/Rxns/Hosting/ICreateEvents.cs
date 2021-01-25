using System;
using System.Linq;
using System.Net.Http;
using System.Reactive.Threading.Tasks;
using Rxns.DDD.Commanding;

namespace Rxns.Hosting
{
    //todo: remove this interface
    public interface ICreateEvents
    {
        IObservable<RxnQuestion[]> ToCommands(IObservable<HttpResponseMessage> response);
    }

    public class EventFactory : ICreateEvents
    {

        public IObservable<RxnQuestion[]> ToCommands(IObservable<HttpResponseMessage> response)
        {
            return Rxn.Create<RxnQuestion[]>(o =>
            {
                return response.Subscribe(msg =>
                {
                    msg.Content.ReadAsStringAsync().ToObservable().Subscribe(result =>
                        {
                            try
                            {
                                if (String.IsNullOrWhiteSpace(result)) return;

                                    o.OnNext(ToCommands(result));
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
