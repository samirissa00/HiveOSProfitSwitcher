@ECHO OFF
TIMEOUT /t 1 /nobreak > NUL
TASKKILL /IM "HiveProfitSwitcher.exe" > NUL
MOVE "C:\Users\micha\source\repos\HiveOSProfitSwitcher\Update\*" "C:\Users\micha\source\repos\HiveOSProfitSwitcher"
START "" /B "C:\Users\micha\source\repos\HiveOSProfitSwitcher\HiveProfitSwitcher\bin\Debug\HiveProfitSwitcher.exe"
