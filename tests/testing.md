implemented a complete test automation endpoint for SshManager. Here's a summary:

What Was Created
Test Server Infrastructure
TestCommand.cs - Command model with 35+ supported actions
TestResponse.cs - Response model with app state/element info classes
ITestServer.cs - Server interface
TestServer.cs - Named pipe server implementation
ITestCommandHandler.cs - Handler interface
TestCommandHandler.cs - ~1200 lines handling all commands
PowerShell Client
scripts/Test-SshManager.ps1 - Full-featured PowerShell module with 25+ cmdlets

How It Works
When you run SshManager in DEBUG mode, a named pipe server starts on SshManagerTestPipe. I can send JSON commands to interact with the app:


# Load the client
. .\scripts\Test-SshManager.ps1

# Check connection
Test-SshManagerConnection

# Take screenshot (I can view this!)
Get-SshManagerScreenshot -SavePath "screen.png"

# Get app state
Get-SshManagerState

# List hosts
Get-SshManagerHosts

# Click elements
Invoke-SshManagerClick -Target "AddHostButton"

# Type text
Set-SshManagerText -Target "HostnameTextBox" -Text "192.168.1.1"

# Connect to host
Connect-SshManagerHost -HostName "My Server"

# Send terminal input
Send-SshManagerTerminalInput -Text "ls -la`n"
Supported Commands
Category	Commands
Health	ping, get-state
Screenshots	screenshot, screenshot-element
UI Discovery	list-elements, get-element, find-element, get-visual-tree
Interaction	click, double-click, right-click, type, clear, focus, send-keys
Properties	get-property, set-property, get-text
Commands	invoke-command, invoke-button
Hosts	get-hosts, select-host, connect-host
Sessions	get-sessions, select-session, disconnect-session, send-to-terminal, get-terminal-output
Dialogs	open-dialog, close-dialog, get-dialogs, select-tab
Wait	wait, wait-for-element
How I Can Use This
Start the app in Debug mode: dotnet run --project src/SshManager.App
I send commands via named pipe to click buttons, take screenshots, check state
I view screenshots to understand UI state (I can render PNG images)
I interact by clicking elements, typing text, connecting to hosts
This gives me the ability to test your app as if I were clicking through it manually.