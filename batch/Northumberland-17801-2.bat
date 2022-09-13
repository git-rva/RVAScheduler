
MOVE %1 %2
echo this is the file: %2\%3
dir %2\%3
rem
rem can process file now that it has been copied
rem
FileProcessor.exe ShellExecute %2 %3 %4
