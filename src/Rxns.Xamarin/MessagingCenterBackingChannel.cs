using System;
using System.Reactive.Disposables;
using Rxns.Interfaces;
using Xamarin.Forms;

namespace Rxns.Xamarin
{
    public class MessagingCenterBackingChannel : LocalBackingChannel<IRxn>
    {
        public class Bridge
        {
            private static Bridge _instance;
            public static Bridge Instance { get { return _instance ?? (_instance = new Bridge());}}
            public static string WrappedMsg = "Bridge";

            public static string Wrap(IRxn @event)
            {
                return new MsgWrapper(@event).ToJson();
            }

            public class MsgWrapper
            {
                public IRxn Msg { get; private set; }

                public MsgWrapper(IRxn toWrap)
                {
                    Msg = toWrap;
                }
            }
        }
        public override IObservable<IRxn> Setup(IDeliveryScheme<IRxn> postman)
        {
            return RxObservable.DfrCreate<IRxn>(o =>
            {
                var localStream = base.Setup(postman).Subscribe(o);
             
                MessagingCenter.Subscribe<Bridge, string>(Bridge.Instance, Bridge.WrappedMsg, (sender, objAsJson) =>
                {
                    var wrapped = objAsJson.FromJson<Bridge.MsgWrapper>();
                    if(wrapped != null)
                    o.OnNext(wrapped.Msg);
                });

                var xamStream = new DisposableAction(() =>
                {
                    MessagingCenter.Unsubscribe<Bridge, string>(Bridge.Instance, Bridge.WrappedMsg);
                    o.OnCompleted();
                });

                return new CompositeDisposable(localStream, xamStream);
            });
        }
    }
}
