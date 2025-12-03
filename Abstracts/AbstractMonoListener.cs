namespace XCommon.Abstracts
{
    public class AbstractMonoListener : MonoBehaviour, IDisposable
    {
        protected List<IDisposable> Disposables = new List<IDisposable>();

        protected virtual void OnDestroy()
        {
            Dispose();
        }

        protected void CreateSubscriber<TMessage>(ISubscriber<TMessage> subscriber, Action<TMessage> handler)
        {
            var d = DisposableBag.CreateBuilder();

            subscriber.Subscribe(handler).AddTo(d);

            Disposables.Add(d.Build());
        }

        public virtual void Dispose()
        {
            Disposables.ForEach(d => d.Dispose());
            Disposables.Clear();
        }
    }
}