@echo off
rem Compile score.exe et score_tot.exe avec le compilateur C# integre a Windows (aucune installation)
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
%CSC% /nologo /optimize+ /out:"%~dp0score.exe" /r:System.Web.Extensions.dll "%~dp0score.cs"
%CSC% /nologo /optimize+ /out:"%~dp0score_tot.exe" /r:System.Web.Extensions.dll "%~dp0score.cs"
