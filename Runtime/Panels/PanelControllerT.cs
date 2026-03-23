namespace Mosaic.UI
{
    public abstract class PanelController<TContext> : PanelController
    {
        public TContext Context { get; private set; }

        internal void SetContext(TContext context)
        {
            Context = context;
        }
    }
}
