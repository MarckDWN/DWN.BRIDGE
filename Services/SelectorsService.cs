using AIBridge.Shared.Models;
using System.Threading.Tasks;

namespace AIBridge.Services;

public interface ISelectorsService
{
    DomSelectors Current { get; }
    Task RefreshFromServerAsync();
}

public class SelectorsService : ISelectorsService
{
    private DomSelectors _current = new();
    
    public DomSelectors Current => _current;

    public async Task RefreshFromServerAsync()
    {
        var client = CloudServiceLocator.Client;
        if (client == null) return;
        
        var selectors = await client.GetSelectorsAsync();
        if (selectors != null)
        {
            _current = selectors;
        }
    }
}
