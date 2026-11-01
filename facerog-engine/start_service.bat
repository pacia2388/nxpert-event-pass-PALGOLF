@echo off
cd /d C:\inetpub\wwwroot\EventPassFr_API_NPAX

"C:\inetpub\wwwroot\EventPassFr_API_NPAX\.venv_ec2\Scripts\python.exe" -m uvicorn app.main:app --host 127.0.0.1 --port 8100 >> "C:\inetpub\wwwroot\EventPassFr_API_NPAX\fastapi.log" 2>&1