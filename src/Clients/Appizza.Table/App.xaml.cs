using Microsoft.Extensions.DependencyInjection;

namespace Appizza.Table;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		window.Resumed += async (_, _) => await TableRuntime.ReconcileAsync(Appizza.Table.Core.ReconciliationTrigger.Resume);
		window.Activated += async (_, _) => await TableRuntime.ReconcileAsync(Appizza.Table.Core.ReconciliationTrigger.Foreground);
		return window;
	}
}
