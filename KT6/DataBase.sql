-- =====================================================================
-- Скрипт базы данных для проекта KT6 (SQL Server / LocalDB)
-- Запускать в SSMS или в окне "Обозреватель объектов SQL Server" в Visual Studio.
-- Скрипт можно запускать повторно: уже существующие объекты не пересоздаются.
-- =====================================================================

IF DB_ID(N'KT6') IS NULL
    CREATE DATABASE KT6;
GO

USE KT6;
GO

-- ЗАДАНИЕ 1: таблица Users с новым полем Age
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        Id    INT PRIMARY KEY,
        Name  NVARCHAR(100),
        Email NVARCHAR(100),
        Age   INT
    );
END
ELSE IF COL_LENGTH(N'dbo.Users', N'Age') IS NULL
BEGIN
    -- таблица уже была создана по старой схеме (без Age) - добавляем поле
    ALTER TABLE dbo.Users ADD Age INT NULL;
END
GO

-- ЗАДАНИЕ 3: таблица истории изменений (для метода с транзакцией)
IF OBJECT_ID(N'dbo.UserHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserHistory (
        HistoryId INT IDENTITY(1, 1) PRIMARY KEY,
        UserId    INT NOT NULL,
        OldName   NVARCHAR(100) NULL,
        OldEmail  NVARCHAR(100) NULL,
        OldAge    INT NULL,
        ChangedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
    );
END
GO

-- Тестовые данные
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
    INSERT INTO dbo.Users (Id, Name, Email, Age) VALUES (1, N'Ivan', N'ivan@mail.com', 25);
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 2)
    INSERT INTO dbo.Users (Id, Name, Email, Age) VALUES (2, N'Anna', N'anna@mail.com', 30);
GO
