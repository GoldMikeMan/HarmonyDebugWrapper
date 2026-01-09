namespace HarmonyDebugWrapper.ToolBox
{
    class ToolBoxHandshake
    {
        public static bool VerifyToolBoxHost()
        {
            const string sentinel = "🔍 Verifying parent is ToolBox...";
            Console.WriteLine(sentinel);
            using var spin = new Spinner("|", "/", "-", "\\");
            if (!Console.IsOutputRedirected) spin.Start("⏳ Waiting for ToolBox");
            long end = Environment.TickCount64 + 5000;
            var readTask = Task.Run(() => Console.ReadLine());
            while (Environment.TickCount64 < end)
            {
                int remaining = (int)Math.Max(0, end - Environment.TickCount64);
                if (readTask.Wait(remaining))
                {
                    var resp = ((readTask.Result ?? "").Trim()).TrimStart('\uFEFF');
                    if (string.Equals(resp, "ToolBox is open", StringComparison.Ordinal))
                    {
                        spin.Stop();
                        Console.WriteLine("✅ ToolBox detected.");
                        return true;
                    }
                }
                Thread.Sleep(10);
            }
            spin.Stop();
            Console.WriteLine("❌ ToolBox required to use this tool.");
            return false;
        }
        sealed class Spinner(params string[] frames) : IDisposable
        {
            readonly string[] frames = frames.Length == 0 ? ["|", "/", "-", "\\"] : frames;
            volatile bool running;
            Thread? t;
            string text = "";
            bool oldCursorVisible = true;
            public void Start(string text)
            {
                if (Console.IsOutputRedirected) return;
                if (running) return;
                this.text = text;
                running = true;
                try { oldCursorVisible = Console.CursorVisible; Console.CursorVisible = false; } catch { }
                t = new Thread(() =>
                {
                    int i = 0;
                    while (running)
                    {
                        try { Console.Write("\r" + this.text + " " + frames[i++ % frames.Length]); } catch { }
                        Thread.Sleep(100);
                    }
                })
                { IsBackground = true };
                t.Start();
            }
            public void Stop()
            {
                if (!running) return;
                running = false;
                try { t?.Join(); } catch { }
                try { int w = Math.Max(1, Console.BufferWidth); Console.Write("\r" + new string(' ', w - 1) + "\r"); } catch { try { Console.Write("\r"); } catch { } }
                try { Console.CursorVisible = oldCursorVisible; } catch { }
            }
            public void Dispose() { Stop(); }
        }
    }
}