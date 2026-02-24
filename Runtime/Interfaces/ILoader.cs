using System.Threading.Tasks;

namespace Xamel.Common.Interfaces
{
    public interface ILoader
    {
        public Task<bool> LoadAsync();
    }   
}