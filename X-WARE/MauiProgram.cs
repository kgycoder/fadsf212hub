using Microsoft.Maui.Handlers;

namespace SYNC;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>();

#if ANDROID
        WebViewHandler.Mapper.AppendToMapping("MyCustomization", (handler, view) =>
        {
            handler.PlatformView.Settings.JavaScriptEnabled = true;
            handler.PlatformView.Settings.DomStorageEnabled = true;
            handler.PlatformView.Settings.MediaPlaybackRequiresUserGesture = false;
            handler.PlatformView.Settings.AllowFileAccess = true;
            handler.PlatformView.Settings.AllowContentAccess = true;
            handler.PlatformView.Settings.AllowFileAccessFromFileURLs = true;
            handler.PlatformView.Settings.AllowUniversalAccessFromFileURLs = true;
        });
#endif

		return builder.Build();
	}
}
