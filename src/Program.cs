namespace OmgAd;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Contains("--self-test")){Environment.ExitCode=SelfTest.Run(args);return;}
        // Repeated launcher clicks must not stack competing topmost windows/hotkeys.
        using var instance=new Mutex(true,"Local\\OMGAD.Desktop.Overlay",out bool firstInstance);
        using var summon=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\OMGAD.Desktop.Summon");
        if(!firstInstance){summon.Set();return;}
        try
        {
            var catalog=Catalog.Load();var stats=Statistics.Load();
            foreach(var a in catalog.Abilities)if(stats.Abilities.TryGetValue(a.Code,out var s))a.Ultimate=s.Ultimate;
            var app=new MainForm(catalog,stats);if(args.Contains("--demo"))app.Demo();
            RegisteredWaitHandle? summonWait=null;
            app.Shown+=(_,_)=>summonWait=ThreadPool.RegisterWaitForSingleObject(summon,(_,_)=>
            {
                if(!app.IsDisposed&&app.IsHandleCreated)app.BeginInvoke(app.ShowConsole);
            },null,Timeout.Infinite,false);
            app.FormClosed+=(_,_)=>summonWait?.Unregister(null);
            if(args.Contains("--live"))app.Shown+=(_,_)=>app.StartLive();
            Application.Run(app);
        }
        catch(Exception e){MessageBox.Show("OMG AD 启动失败：\n"+e.Message+"\n请保留程序旁的 data 文件夹。","OMG AD");}
    }
}
