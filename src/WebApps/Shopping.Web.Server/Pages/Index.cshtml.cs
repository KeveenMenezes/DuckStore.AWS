namespace Shopping.Web.Server.Pages;

public class IndexModel(ILogger<IndexModel> logger) : PageModel
{
    private readonly ILogger<IndexModel> _logger = logger;

    public void OnGet()
    {

    }
}
