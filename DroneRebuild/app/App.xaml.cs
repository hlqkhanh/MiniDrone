using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace DroneRebuild;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();MainWindow=window;
        if(e.Args.Length==2 && e.Args[0]=="--connection-test")
        {
            window.Left=-10000;window.ShowInTaskbar=false;window.Show();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
                try{File.WriteAllText(e.Args[1],await window.CheckConnections());Shutdown(0);}
                catch(Exception ex){File.WriteAllText(e.Args[1],ex.ToString());Shutdown(1);}
            }));
        }
        else if(e.Args.Length>=3 && e.Args[0]=="--smoke")
        {
            if(e.Args.Length>=5) { window.Width=double.Parse(e.Args[3],System.Globalization.CultureInfo.InvariantCulture);window.Height=double.Parse(e.Args[4],System.Globalization.CultureInfo.InvariantCulture); }
            window.Left=-10000;window.ShowInTaskbar=false;
            window.Show();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=>
            {
                try {
                    await window.LoadDroneModel();
                    window.LoadReplay(e.Args[1]);
                    if(e.Args.Length>=6)window.SetPreviewScenario(e.Args[5]);
                    window.UpdateLayout();
                    var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                    image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                    using var output=File.Create(e.Args[2]);encoder.Save(output);
                    Shutdown(0);
                }catch(Exception ex){File.WriteAllText(e.Args[2]+".error.txt",ex.ToString());Shutdown(1);}
            }));
        }
        else window.Show();
    }
}
