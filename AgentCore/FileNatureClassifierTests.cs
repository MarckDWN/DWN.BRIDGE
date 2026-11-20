using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AIBridge.AgentCore
{
    public static class FileNatureClassifierTests
    {
        public static (bool AllPassed, int PassedCount, int TotalCount, List<string> Log) RunAcceptanceTest()
        {
            var log = new List<string>();
            string tempFolder = Path.Combine(Path.GetTempPath(), "FileNatureAcceptanceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);

            int passed = 0;
            int total = 0;

            try
            {
                // --- 1. SORGENTI DI ESEMPIO CON DIVERSE ESTENSIONI NOTORIE ---
                var sourceCodeCases = new Dictionary<string, string>
                {
                    { "App.cs", "using System;\nnamespace MyApp { class Program { static void Main() { Console.WriteLine(\"Hello\"); } } }" },
                    { "script.py", "import os\ndef main():\n    print('Hello World')\nif __name__ == '__main__':\n    main()\n" },
                    { "index.js", "const express = require('express');\nconst app = express();\napp.get('/', (req, res) => res.send('OK'));\napp.listen(3000);\n" },
                    { "app.ts", "interface User { id: number; name: string; }\nfunction getUser(id: number): User { return { id, name: 'Admin' }; }\n" },
                    { "Component.jsx", "import React from 'react';\nexport default function Component() { return <div>Hello JSX</div>; }\n" },
                    { "Widget.tsx", "import React from 'react';\ninterface Props { title: string; }\nexport const Widget: React.FC<Props> = ({title}) => <h1>{title}</h1>;\n" },
                    { "main.cpp", "#include <iostream>\nint main() { std::cout << \"Hello C++\" << std::endl; return 0; }\n" },
                    { "utils.c", "#include <stdio.h>\nvoid print_msg() { printf(\"Hello C\\n\"); }\n" },
                    { "header.h", "#ifndef HEADER_H\n#define HEADER_H\nvoid print_msg();\n#endif\n" },
                    { "header.hpp", "#pragma once\nclass Helper { public: void Run(); };\n" },
                    { "Main.java", "public class Main { public static void main(String[] args) { System.out.println(\"Java\"); } }\n" },
                    { "main.go", "package main\nimport \"fmt\"\nfunc main() { fmt.Println(\"Go\") }\n" },
                    { "main.rs", "fn main() { println!(\"Hello Rust\"); }\n" },
                    { "index.php", "<?php echo 'Hello PHP'; ?>\n" },
                    { "app.rb", "puts 'Hello Ruby'\n" },
                    { "schema.sql", "CREATE TABLE Users (Id INT PRIMARY KEY, Username VARCHAR(50));\nCREATE INDEX IX_Users ON Users(Username);\n" },
                    { "MainWindow.xaml", "<Window x:Class=\"MyApp.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid></Grid></Window>\n" },
                    { "Test.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>\n" },
                    { "Test.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\n" },
                    { "styles.css", "body { margin: 0; padding: 0; background-color: #f0f0f0; font-family: sans-serif; }\n" },
                    { "styles.scss", "$primary: #333;\nbody { color: $primary; header { font-weight: bold; } }\n" },
                    { "styles.less", "@base: #f9f9f9;\n#header { color: @base; }\n" },
                    { "appsettings.json", "{\n  \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } },\n  \"AllowedHosts\": \"*\"\n}\n" },
                    { "package.json", "{\n  \"name\": \"my-app\",\n  \"version\": \"1.0.0\",\n  \"scripts\": { \"start\": \"node index.js\" },\n  \"dependencies\": { \"express\": \"^4.18.0\" }\n}\n" },
                    { "web.config", "<configuration><system.webServer><handlers></handlers></system.webServer></configuration>\n" },
                    // PAGINE WEB REALMENTE HTML (NON SEGNALIBRI)
                    { "index.html", "<!DOCTYPE html>\n<html lang=\"it\">\n<head>\n<meta charset=\"UTF-8\">\n<title>Mio Sito Web</title>\n</head>\n<body>\n<h1>Benvenuto nel mio sito web!</h1>\n<div class=\"content\">Contenuto della pagina.</div>\n</body>\n</html>\n" },
                    { "esportazione.html", "<!DOCTYPE html>\n<html lang=\"it\">\n<head><title>Pagina Esportata Cliente</title></head>\n<body>\n<div class=\"page\"><h1>Report Sito Web</h1><p>Ecco la struttura del layout.</p></div>\n</body>\n</html>\n" }
                };

                foreach (var kvp in sourceCodeCases)
                {
                    total++;
                    string filePath = Path.Combine(tempFolder, kvp.Key);
                    File.WriteAllText(filePath, kvp.Value, Encoding.UTF8);

                    var result = FileNatureClassifier.Classify(filePath);
                    if (result == FileNature.SourceCode)
                    {
                        passed++;
                        log.Add($"[PASS] {kvp.Key} -> Classified correctly as SourceCode");
                    }
                    else
                    {
                        log.Add($"[FAIL] {kvp.Key} -> Expected SourceCode, got {result}");
                    }
                }

                // --- 2. FILE DI DATI / EXPORT UTENTE ---
                var userDataExportCases = new Dictionary<string, string>
                {
                    // Netscape Bookmarks (con vari nomi arbitrari)
                    { "bookmarks.html", "<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE>\n<H1>Bookmarks</H1>\n<DL><p>\n<DT><A HREF=\"https://google.com\" ADD_DATE=\"1700000000\">Google</A>\n" },
                    { "esportazione_segnalibri.html", "<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<!-- Exported from browser -->\n<DL><p>\n<DT><H3 ADD_DATE=\"1700000000\">Barra dei segnalibri</H3>\n<DL><p>\n<DT><A HREF=\"https://github.com\" ADD_DATE=\"1700000001\">GitHub</A>\n" },
                    { "mie_pagine.html", "<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<TITLE>Lesezeichen</TITLE>\n<DL><p>\n<DT><A HREF=\"https://aruba.it\" ADD_DATE=\"1700000002\">Aruba</A>\n" },
                    // CSV / Tabular Data
                    { "dati_clienti.csv", "ID,Nome,Cognome,Email,Telefono\n1,Mario,Rossi,mario@example.com,333123456\n2,Luigi,Verdi,luigi@example.com,333654321\n3,Anna,Bianchi,anna@example.com,333999888\n4,Giuseppe,Neri,giuseppe@example.com,333777666\n" },
                    { "export.txt", "ColonnaA;ColonnaB;ColonnaC;ColonnaD\nValore1;Valore2;Valore3;Valore4\nValore5;Valore6;Valore7;Valore8\nValore9;Valore10;Valore11;Valore12\n" },
                    // Logs
                    { "server.log", "2026-07-23 14:00:00 [INFO] Server started on port 8080\n2026-07-23 14:00:01 [DEBUG] Connecting to database\n2026-07-23 14:00:02 [WARN] High memory usage detected\n2026-07-23 14:00:03 [ERROR] Database connection failed\n" },
                    // SQL Data Dump
                    { "backup_dump.sql", "INSERT INTO `users` (`id`, `username`, `email`) VALUES (1, 'user1', 'u1@test.com');\nINSERT INTO `users` (`id`, `username`, `email`) VALUES (2, 'user2', 'u2@test.com');\nINSERT INTO `users` (`id`, `username`, `email`) VALUES (3, 'user3', 'u3@test.com');\n" },
                    // Chat Serialized Log
                    { "chat_history.json", "{\n  \"messages\": [\n    {\"sender\": \"Alice\", \"text\": \"Ciao\"},\n    {\"sender\": \"Bob\", \"text\": \"Hello\"}\n  ]\n}\n" }
                };

                foreach (var kvp in userDataExportCases)
                {
                    total++;
                    string filePath = Path.Combine(tempFolder, kvp.Key);
                    File.WriteAllText(filePath, kvp.Value, Encoding.UTF8);

                    var result = FileNatureClassifier.Classify(filePath);
                    if (result == FileNature.UserDataExport)
                    {
                        passed++;
                        log.Add($"[PASS] {kvp.Key} -> Classified correctly as UserDataExport");
                    }
                    else
                    {
                        log.Add($"[FAIL] {kvp.Key} -> Expected UserDataExport, got {result}");
                    }
                }
            }
            finally
            {
                try { Directory.Delete(tempFolder, true); } catch { }
            }

            bool allPassed = (passed == total && total > 0);
            return (allPassed, passed, total, log);
        }
    }
}
