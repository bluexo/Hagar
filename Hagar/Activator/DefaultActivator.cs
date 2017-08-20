namespace Hagar.Activator
{
    public class DefaultActivator<T> : IActivator<T>
    {
        public T Create()
        {
            return System.Activator.CreateInstance<T>();
        }
    }
}