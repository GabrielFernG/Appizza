namespace Appizza.Table;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(MenuPage), typeof(MenuPage));
		Routing.RegisterRoute(nameof(ProductConfigurationPage), typeof(ProductConfigurationPage));
		Routing.RegisterRoute(nameof(CartPage), typeof(CartPage));
		Routing.RegisterRoute(nameof(DeliveryPage), typeof(DeliveryPage));
	}
}
