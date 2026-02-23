# SQLsense - Intelligent SQL Assistant for SSMS 22

SQLsense is an intelligent query assistant engineered specifically for **SQL Server Management Studio (SSMS) 22**. Designed to boost developer productivity, it brings a modern, highly responsive, "Redgate SQL Prompt-like" experience natively into SSMS, accelerating your coding workflow and enforcing team standards.

## 🚀 Key Features

### 1. Real-Time Keyword Casing
- **Instant Transformation:** As you type, over 180+ T-SQL keywords (like `select`, `from`, `where`) are instantly transformed to your preferred casing (Upper, Lower, Pascal, or LeaveAsIs) the moment you hit Space, Tab, or Enter.
- **Smart Context Awareness:** Designed to be culturally aware and robust against edge cases (e.g., Turkish `I/İ` character mappings).

### 2. Professional T-SQL Formatting
- **ScriptDom Integration:** Leverages Microsoft's official SQL Server 2022 (v160) parser engine (`Microsoft.SqlServer.TransactSql.ScriptDom`) for flawless, professional-grade code alignment.
- **On-Demand:** Format any active document instantly via `Tools -> SQLsense -> Format SQL`.

### 3. Rapid Snippets Engine
- **Shortcode Expansion:** Boost your speed with customizable shortcuts. Type `ssf` and press **Space/Tab** to instantly expand it to `SELECT * FROM `.
- **Configurable JSON:** Easily manage and add your own snippets via the underlying `snippets.json` configuration file.

### 4. SQL Guardian (Background Linting)
- **Real-Time Analysis:** An intelligent background worker that continuously parses your T-SQL.
- **Error List Integration:** Immediately highlights anti-patterns and code smells (e.g., *“SELECT * usage detected”*, *“UPDATE statement missing WHERE clause”*) directly inside the SSMS Error List window.

### 5. Airtight Session Recovery
- **Never Lose Work:** A highly robust SQLite-backed session manager that tracks your active tabs.
- **Ghost-Session Proof:** Intercepts the core package `QueryClose` event to perform a 100% accurate, millisecond-precise synchronization of your open documents prior to SSMS shutdown. 
- Restores all your active and unsaved queries the next time you open SSMS.

### 6. Comprehensive Settings UI
- **Native Integration:** Accessible via `Tools -> Options -> SQLsense`.
- **Full Control:** Toggle Session Recovery, globally enable/disable SQL Guardian, and choose your exact Keyword Casing preferences out-of-the-box.

---

## 🛠️ Installation & Build

SQLsense requires **Visual Studio 2022 (MSBuild 17.0+)** and **.NET Framework 4.8**.

1. Clone the repository.
2. Build the `SQLsense.csproj` project.
3. Rapid deployment to your local SSMS 22 environment is fully automated via the included `Deploy-To-SSMS.ps1` PowerShell script.

---
**SQLsense Team** - *Writing SQL is finally enjoyable again.*
