using AprilTagPose.Services;

namespace AprilTagPose;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return CommandLineRunner.Run(args);
        }

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"程序启动失败：\r\n{exception.Message}\r\n\r\n{exception}",
                "ArUco 位姿识别",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
