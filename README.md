# SQLsense - Intelligent SQL Assistant for SSMS 22

SQLsense is an advanced intelligent query assistant engineered specifically for **SQL Server Management Studio (SSMS) 22**. Designed to boost developer productivity, it brings a modern, highly responsive, "SQL Prompt-style" experience natively into SSMS, accelerating your coding workflow while strictly enforcing team structural standards.

## 🚀 Key Features

### 1. Smart IntelliSense & Auto-Complete
- **Context-Aware Suggestions:** Prioritizes Tables and Views when typing `FROM` or `JOIN`. Heavily prioritizes Columns automatically when typing after `SELECT`, `WHERE`, `ON`, and `SET`.
- **Intelligent Alias Mapping:** When you declare table aliases (e.g. `FROM Orders O`), SQLsense remembers them. Typing `O.` or picking a column belonging to `Orders` automatically prepends your exact alias case seamlessly (`O.OrderId`).
- **Mid-String & CamelCase Matching:** Type `ordid` to instantly match `OrderId`, or type anywhere in the middle of a string to easily find columns within chaotic schemas.
- **Auto-Join Generation:** Typing `ON` after a `JOIN` automatically builds out the relationship bridging based on foreign key heuristics (e.g. `ON O.CustomerId = C.Id`).
- **Wildcard Expansion:** Type `SELECT *` and select the `*` from the IntelliSense popup to instantly expand the wildcard into a comma-separated list of all relevant columns.

### 2. Real-Time Keyword Casing
- **Instant Transformation:** As you type, over 180+ T-SQL keywords (like `select`, `from`, `where`) are instantly transformed to your preferred casing (Upper, Lower, Pascal, or LeaveAsIs) the moment you hit Space, Tab, or Enter.

### 3. Professional T-SQL Formatting
- **ScriptDom Integration:** Leverages Microsoft's official SQL Server 2022 (v160) parser engine (`Microsoft.SqlServer.TransactSql.ScriptDom`) for flawless, professional-grade code alignment.
- **On-Demand:** Format any active document instantly via `Tools -> SQLsense -> Format SQL`.

### 4. Rapid Snippets Engine
- **Shortcode Expansion:** Boost your speed with customizable shortcuts. Type `ssf` and press **Space/Tab** to instantly expand it to `SELECT * FROM `.

### 5. SQL Guardian (Background Linting)
- **Real-Time Analysis:** An intelligent background worker continuously parses your T-SQL. Highlights anti-patterns and code smells (e.g., *“SELECT * usage detected”*, *“UPDATE missing WHERE clause”*) directly inside the SSMS Error List window.

### 6. Airtight Session Recovery
- **Never Lose Work:** A highly robust SQLite-backed session manager that tracks your active tabs and restores them precisely after an SSMS crash or restart. Ghost-session proof.

---

## 🛠️ Installation & Build

SQLsense requires **Visual Studio 2022 (MSBuild 17.0+)** and **.NET Framework 4.8**.

The extension is now bundled and deployed via standard VSIX for modern SSMS 21/22 integration.

1. Download the latest `SQLsense.vsix` or build the `SQLsense.csproj` project yourself.
2. Double-click the `.vsix` file to launch the **VSIX Installer** and follow the prompts to install it into SSMS 22.
3. Rapid deployment to your local SSMS 22 environment during development is fully automated via the included `Deploy-To-SSMS.ps1` PowerShell script.

---

## 🗑️ Uninstallation

### Standard Uninstallation (Recommended)
1. Close SSMS completely
2. Open the **Visual Studio Installer** (Windows Search → "Visual Studio Installer")
3. Find **SQL Server Management Studio 21** or **SQL Server Management Studio 22** and click **Modify**
4. Go to the **Individual Components** tab
5. Search for **"SQLsense"** and uncheck it
6. Click **Modify** and wait for the process to complete
7. Start SSMS

### Remove Leftover Extension Files (If Necessary)
Sometimes it is necessary to manually delete leftover extension files and clear the cache if the standard unpacker fails.

1. Close SSMS completely
2. Open Windows Explorer and navigate to one of these locations:  
   - **SSMS 21:** `C:\Program Files\Microsoft SQL Server Management Studio 21\Release\Common7\IDE\Extensions\`
   - **SSMS 22:** `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\`
3. Look for a folder containing `SQLsense*` files and **delete it** *(Note: The inner folder name might be a random string like `ul4254xk.zre`)*
4. Please check also these AppData folders and delete any `SQLsense` folders:
   - **SSMS 21:** `%LocalAppData%\Microsoft\SSMS\21.0_*\Extensions\`
   - **SSMS 22:** `%LocalAppData%\Microsoft\SSMS\22.0_*\Extensions\`

### Rebuild the Extension Cache
If you performed a manual deletion, you must rebuild the SSMS metadata cache:

1. Open a **Command Prompt** as Administrator.
2. Run the following command corresponding to your version:
   - **SSMS 21:** `"C:\Program Files\Microsoft SQL Server Management Studio 21\Release\Common7\IDE\ssms.exe" /setup`
   - **SSMS 22:** `"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\ssms.exe" /setup`
3. Wait for the command to complete (no window will open, it usually takes ~1 second).
4. Start SSMS normally.

---
**SQLsense Team** - *Writing SQL is finally enjoyable again.*
