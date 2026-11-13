using System.Data;
using KT6;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;

namespace KT6.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly FileLogger _logger;

        public UsersController(IConfiguration configuration, FileLogger logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // Строка подключения берётся из appsettings.json (раздел ConnectionStrings).
        // Свойство читается внутри try, поэтому её отсутствие тоже обрабатывается и логируется.
        private string ConnectionString =>
            _configuration.GetConnectionString("YourConnectionString")
            ?? throw new InvalidOperationException(
                "Строка подключения 'YourConnectionString' не найдена в appsettings.json.");

        // =====================================================================
        // ЗАДАНИЕ 1. Обновление нескольких полей (добавлен Age)
        // PUT api/users/1?name=Ivan&email=ivan@mail.com&age=30
        // (+ задания 4 и 5: обработка исключений и запись в лог)
        // =====================================================================
        [HttpPut("{id:int}")]
        public IActionResult UpdateUser(int id, string name, string email, [BindRequired] int age)
        {
            const string action = "UpdateUser";

            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    string query = "UPDATE Users SET Name = @Name, Email = @Email, Age = @Age WHERE Id = @Id";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
                        command.Parameters.Add("@Email", SqlDbType.NVarChar, 100).Value = email;
                        command.Parameters.Add("@Age", SqlDbType.Int).Value = age;

                        connection.Open();
                        int result = command.ExecuteNonQuery();

                        if (result < 1)
                        {
                            _logger.Log(action, id, false, "Обновление не удалось");
                            return BadRequest("Обновление не удалось.");
                        }
                    }
                }

                _logger.Log(action, id, true, "Данные обновлены");
                return Ok("Данные пользователя обновлены.");
            }
            catch (SqlException ex)
            {
                return DatabaseError(action, id, ex);
            }
            catch (Exception ex)
            {
                return ServerError(action, id, ex);
            }
        }

        // =====================================================================
        // ЗАДАНИЕ 2. Условное обновление: email меняется, только если имя совпадает
        // PUT api/users/1/email?name=Ivan&newEmail=new@mail.com
        // =====================================================================
        [HttpPut("{id:int}/email")]
        public IActionResult UpdateEmailIfNameMatches(int id, string name, string newEmail)
        {
            const string action = "UpdateEmailIfNameMatches";

            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    // Условие "AND Name = @Name" - обновление произойдёт, только если имя совпало
                    string query = "UPDATE Users SET Email = @Email WHERE Id = @Id AND Name = @Name";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
                        command.Parameters.Add("@Email", SqlDbType.NVarChar, 100).Value = newEmail;

                        connection.Open();
                        int result = command.ExecuteNonQuery();

                        if (result < 1)
                        {
                            _logger.Log(action, id, false, "Пользователь не найден или имя не совпадает");
                            return NotFound("Пользователь не найден или имя не совпадает. Email не изменён.");
                        }
                    }
                }

                _logger.Log(action, id, true, "Email обновлён");
                return Ok("Email обновлён.");
            }
            catch (SqlException ex)
            {
                return DatabaseError(action, id, ex);
            }
            catch (Exception ex)
            {
                return ServerError(action, id, ex);
            }
        }

        // =====================================================================
        // ЗАДАНИЕ 3. Транзакция: сохранить старые данные в историю + обновить пользователя.
        // Либо выполнятся оба шага, либо ни один.
        // PUT api/users/1/with-history?name=Ivan&email=ivan@mail.com&age=30
        // =====================================================================
        [HttpPut("{id:int}/with-history")]
        public IActionResult UpdateUserWithHistory(int id, string name, string email, [BindRequired] int age)
        {
            const string action = "UpdateUserWithHistory";

            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    connection.Open();

                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Шаг 1: копируем текущие данные в историю.
                            // UPDLOCK блокирует строку до конца транзакции, чтобы никто не изменил её между шагами.
                            string historyQuery =
                                @"INSERT INTO UserHistory (UserId, OldName, OldEmail, OldAge)
                                  SELECT Id, Name, Email, Age FROM Users WITH (UPDLOCK) WHERE Id = @Id";

                            int saved;
                            using (SqlCommand historyCommand = new SqlCommand(historyQuery, connection, transaction))
                            {
                                historyCommand.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                saved = historyCommand.ExecuteNonQuery();
                            }

                            // Шаг 2: обновляем пользователя (только если он существует)
                            int updated = 0;
                            if (saved > 0)
                            {
                                string updateQuery =
                                    "UPDATE Users SET Name = @Name, Email = @Email, Age = @Age WHERE Id = @Id";

                                using (SqlCommand updateCommand = new SqlCommand(updateQuery, connection, transaction))
                                {
                                    updateCommand.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                                    updateCommand.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
                                    updateCommand.Parameters.Add("@Email", SqlDbType.NVarChar, 100).Value = email;
                                    updateCommand.Parameters.Add("@Age", SqlDbType.Int).Value = age;
                                    updated = updateCommand.ExecuteNonQuery();
                                }
                            }

                            if (saved < 1 || updated < 1)
                            {
                                // Пользователя нет - откатываем всё, что успели сделать
                                transaction.Rollback();
                                _logger.Log(action, id, false, "Пользователь не найден, транзакция отменена");
                                return NotFound("Пользователь не найден.");
                            }

                            // Оба шага успешны - фиксируем
                            transaction.Commit();
                        }
                        catch
                        {
                            // Любая ошибка внутри транзакции - отменяем ВСЕ изменения
                            try
                            {
                                transaction.Rollback();
                            }
                            catch
                            {
                                // Соединение уже разорвано: SQL Server сам отменит незавершённую транзакцию
                            }

                            throw; // дальше ошибку обработают внешние catch (ответ API + лог)
                        }
                    }
                }

                _logger.Log(action, id, true, "Данные обновлены, старая версия сохранена в историю");
                return Ok("Данные обновлены, старая версия сохранена в историю.");
            }
            catch (SqlException ex)
            {
                return DatabaseError(action, id, ex);
            }
            catch (Exception ex)
            {
                return ServerError(action, id, ex);
            }
        }

        // =====================================================================
        // ЗАДАНИЯ 4 и 5. Обработка исключений + логирование + проверка входных данных
        // PUT api/users/1/safe?name=Ivan&email=ivan@mail.com&age=30
        // =====================================================================
        [HttpPut("{id:int}/safe")]
        public IActionResult UpdateUserSafe(int id, string? name, string? email, int? age)
        {
            const string action = "UpdateUserSafe";

            // Проверка входных данных до обращения к БД
            // (параметры допускают null, чтобы даже неполный запрос дошёл сюда и попал в лог)
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
                string.IsNullOrWhiteSpace(email) || email.Length > 100 ||
                age is null || age < 0 || age > 150)
            {
                _logger.Log(action, id, false, "Некорректные входные данные");
                return BadRequest("Некорректные входные данные.");
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    string query = "UPDATE Users SET Name = @Name, Email = @Email, Age = @Age WHERE Id = @Id";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
                        command.Parameters.Add("@Email", SqlDbType.NVarChar, 100).Value = email;
                        command.Parameters.Add("@Age", SqlDbType.Int).Value = age.Value;

                        connection.Open();
                        int result = command.ExecuteNonQuery();

                        if (result < 1)
                        {
                            _logger.Log(action, id, false, "Пользователь не найден");
                            return NotFound("Пользователь не найден.");
                        }
                    }
                }

                _logger.Log(action, id, true, "Данные обновлены");
                return Ok("Данные пользователя обновлены.");
            }
            catch (SqlException ex)
            {
                return DatabaseError(action, id, ex);
            }
            catch (Exception ex)
            {
                return ServerError(action, id, ex);
            }
        }

        // ---------------------------------------------------------------------
        // ЗАДАНИЕ 4. Единый ответ API при ошибках + запись ошибки в лог (задание 5)
        // ---------------------------------------------------------------------

        // Ошибки подключения к БД и выполнения SQL-запроса
        private IActionResult DatabaseError(string action, int id, SqlException ex)
        {
            _logger.Log(action, id, false, $"SqlException #{ex.Number}: {ex.Message}");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Ошибка при работе с базой данных. Попробуйте позже.");
        }

        // Всё остальное (например, нет строки подключения в конфиге)
        private IActionResult ServerError(string action, int id, Exception ex)
        {
            _logger.Log(action, id, false, $"{ex.GetType().Name}: {ex.Message}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Внутренняя ошибка сервера.");
        }
    }
}
