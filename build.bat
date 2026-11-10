@echo off
rem Compile score.exe avec le compilateur C# integre a Windows (aucune installation)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /platform:anycpu /out:"%~dp0score.exe" /r:System.Web.Extensions.dll "%~dp0score.cs"
