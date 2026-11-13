using System.Text;

namespace KT6
{
    // ЗАДАНИЕ 5: простая система логирования.
    // Каждая запись: время | действие | UserId | результат | подробности.
    // Файл: <папка проекта>\App_Data\update_log.txt
    public class FileLogger
    {
        private readonly object _lock = new();
        private readonly string _path;
        private readonly ILogger<FileLogger> _systemLogger;

        public FileLogger(IWebHostEnvironment env, ILogger<FileLogger> systemLogger)
        {
            _path = Path.Combine(env.ContentRootPath, "App_Data", "update_log.txt");
            _systemLogger = systemLogger;
        }

        public void Log(string action, int userId, bool success, string details)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {action} | UserId={userId} | " +
                          $"{(success ? "УСПЕШНО" : "ОШИБКА")} | {details.ReplaceLineEndings(" ")}";

            try
            {
                // lock нужен, потому что запросы приходят параллельно
                lock (_lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // Ошибка логирования не должна ломать работу API - пишем её только в консоль
                _systemLogger.LogWarning(ex, "Не удалось записать в файл лога {Path}", _path);
            }
        }
    }
}
