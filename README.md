# FlexiFit API - Personalized Fitness & Nutrition Planning System

An inclusive, high-performance, and scalable **REST Web API** engineered to power the FlexiFit personalized fitness ecosystem. This system utilizes a custom, rule-based **Mathematical Logic Algorithm** to dynamically generate workout and nutirition plan, featuring specialized programs designed specifically for injured users.

Built using an **AI-Assisted Engineering workflow**, Large Language Models (LLMs) were leveraged during development to refine mathematical models and accelerate syntactical optimization, ensuring a cost-efficient, deterministic, and highly reliable backend architecture.

---

## 🛠️ Tech Stack

* **Backend Framework:** .NET 8.0 (C#)
* **Database Access:** Entity Framework Core & Dapper
* **Authentication:** JWT Bearer Token & Firebase Admin SDK
* **API Documentation:** Swagger UI (Swashbuckle)

---  

## System Modules & API Endpoints Layout
The architecture enforces domain separation across multiple specialized controllers verified within the preview panel:

### 🔐 Auth & Profile Management
*   `Auth` - Manages token distribution for secure client sessions.
*   `Profile` & `ProfileStatus` - Tracks target biometric indices and user physical stats.
*   `Users` & `SettingsAccount` - Safeguards user identity data and credential updates.

### 📅 Calendar & Mobile Delivery
*   `Calendar` - Orchestrates activity logging and scheduled program routines.
*   `Mobile` - Low-latency endpoint routing optimized for mobile integration.

### 🥗 Intelligent Nutrition Engine
*   `Nutrition` - Calculates dynamic macro boundaries and nutritional distributions through math equations.

### 🦾 Workout & Progression Tracks
*   `Program` & `UserProgram` - Translates raw body metrics into progressive workout levels.
*   `Workout` - Applies the custom injury filtering such as for Rehab Program to ensure client training parameters stay safe yet active.

### 🔔 System Utilities
*   `Notifications` - Handles internal telemetry updates and system alerts.
*   `Test` - Built-in integration checkpoints to verify overall server and deployment health.

---

## Core System Features

The **FlexiFit API** powers the app with secure data management, user authentication, and adaptive programs:

### 1. 🔐 Robust User Authentication & Security
* **Firebase Sign-In Integration:** Verifies incoming user accounts safely using token payloads passed from the Firebase client layer.
* **Hybrid Login:** Validates matched `email` and `firebaseUid` combinations directly from the internal system database.
* **Secure JWT Access Issuance:** Generates a custom cryptographic JSON Web Token (JWT) with precise expiry times upon a successful login.
* **Role-Based Access Control:** Protects backend data routes by restricting execution access to authenticated `USER` or `ADMIN` roles using custom padlock locks.

### 2. 🗓️ Intelligent Calendar & Workout Logging
* **Activity Tracking Matrix:** Stores and retrieves unique calendar schedules associated with user fitness goals.
* **Persistence & Queries:** Connects to specific schemas (`usr_users` table data) to adapr based on user's activity historical metrics.

### 3. 🖥️ Cross-Origin Resource Sharing (CORS) Configuration
* **Admin Panel Bridge:** Explicitly opens secure communication ports (`http://localhost:5100`) allowing administrative dashboards to securely read FlexiFit assets without security blocks.
* **High-Performance Memory Caching:** Minimizes heavy continuous hits to the database server by saving fast, recurring framework checks directly into the local memory cache layer.

---

## Database Architecture & Modular Schemas
The database is engineered with strict domain segregation using specific naming conventions and table prefixes to maintain peak query efficiency and system reliability across over 40 relational tables:

*   **`usr_` (User & Telemetry Management):** Handles secure device tokens, notification settings, onboarding details, and historical session logs. Features version-controlled profile backups (`usr_user_profile_versions`) to maintain clean historical audit trails.
*   **`ntr_` (Intelligent Nutrition Engine):** Maps allergy matrix systems (`ntr_user_allergies`), food properties, daily meal logs, and automated caloric/macronutrient target structures calculated by backend equations.
*   **`wrk_` & `wkt_` (Workouts & Progressions):** Houses the relational workout calendars, template, and difficulty progression instances (Tiers 1 to 10) driven by the system's rule-based adaptive logic filters.

---

## Developer Setup & Local Installation Guide

Welcome, developers! To clone, configure, and test this API locally on your machine, please follow these step-by-step instructions.

### 📋 Prerequisites
Before starting, ensure you have the following installed:
- [.NET Core 8.0 SDK]
- [Visual Studio 2022] / VS Code
- [Microsoft SQL Server Express] (or LocalDB)

### 🚀 1. Clone the Repository
Open your terminal or command prompt and run:
```bash
git clone https://github.com/CjConvento/FlexiFit.Api
cd FlexiFit.Api
```

### 🔑 2. Environment Variables & Security Configuration
For safety and compliance, production credentials and security tokens are excluded from this source control. You must provide your own credentials inside the `appsettings.Development.json` file found in the Web API project folder following the appsettings template:

*   **JWT Authentication:**
    - Replace the placeholder under `JwtSettings:Secret` with your own secure, 256-bit string key.
*   **Firebase Service Account:**
    - Generate a private key JSON from your own Firebase Console (**Project Settings > Service Accounts**).
    - Download the file, save it securely inside your local project directory, and map its path under the `Firebase` block configuration.

### 🗄️ 3. Database Connection String & Enterprise Schema Setup
To safely reconstruct the database schema without needing heavy backup files, update the connection strings:
1. Locate the `ConnectionStrings` block in your local `appsettings.Development.json`.
2. Update the values with your local SQL Server details (Server Name, Database Name, and Authentication preferences).

---

## Running the Application

Open your terminal inside the root project directory and execute the following commands to initialize the web host pipeline:

1. **Clear old build artifacts:**
   ```powershell
   dotnet clean
   ```

2. **Launch the Web API:**
   ```powershell
   dotnet run
   ```

3. **Access Swagger UI Catalog:**
   Once the terminal displays the output log *Application started*, open your web browser and navigate to the documentation portal link:
   👉 **[http://localhost:5160/swagger](http://localhost:5160/swagger)**

---

## 📸 API Documentation Preview
Below is the layout map of the exposed modules as seen on the interactive Swagger UI layer:

![FlexiFit API Swagger Documentation Preview](wwwroot/images/swagger-endpoints.png)
