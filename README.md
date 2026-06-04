# Ping 💬

> A real-time private messaging web application built with ASP.NET Core, SignalR, and MySQL.

**Ping** is a self-hostable, privacy-focused messenger. Register an account, find another user by their login, and chat with them in real time — with text, voice messages, images, GIFs, and emoji reactions. Because you run it on your own server, your conversations stay under your control instead of living on someone else's cloud.

---

## ✨ Features

- **Real-time messaging** — messages, read receipts, and online status delivered instantly over WebSocket via SignalR
- **Secure authentication** — passwords stored as BCrypt hashes, cookie-based sessions, route authorization
- **Rich media** — voice messages (recorded via the MediaRecorder API), images (compressed client-side with the Canvas API), and GIFs (powered by the Giphy API)
- **Emoji reactions** — react to any message; one reaction per emoji per user, enforced at the database level
- **Message management** — edit or delete your own messages within a time window
- **User search & private chats** — find users by login and start one-to-one conversations (no duplicate chats per pair)
- **Profile & avatars** — edit your details, change your password, upload or remove an avatar (with auto-generated colored placeholders)
- **SPA-like navigation** — content swaps via AJAX without full page reloads

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | ASP.NET Core MVC (C#, .NET 8) |
| Real-time | SignalR (WebSocket) |
| Data access | Entity Framework Core + Pomelo provider (database-first) |
| Database | MySQL 8 |
| Frontend | Razor views, vanilla JavaScript (ES modules), CSS |
| Auth | BCrypt.Net-Next, cookie authentication |
| External APIs | Giphy (GIF search) |
| Client libraries | twemoji, emoji-picker-element, intl-tel-input |

---

## 🏗️ Architecture

Ping follows a **layered, modular-monolith** architecture with a dual communication channel:

- **HTTP / AJAX** for standard operations (navigation, history, profile, uploads) — partial views are returned for AJAX requests (detected via the `X-Requested-With` header)
- **SignalR / WebSocket** (`/chatHub`) for everything real-time — sending messages, read receipts, reactions, and presence

```
┌─────────────────────────────────────────────┐
│  Presentation  — Razor views + JS modules    │
│  (chat.js, voice.js, image.js, media-picker) │
└───────────────┬───────────────┬─────────────┘
        AJAX/HTTP │               │ WebSocket
┌─────────────────▼───────────────▼─────────────┐
│  Application   — Controllers + ChatHub (SignalR)│
│  Account · Home · Messages · Settings · ChatHub │
└───────────────────────┬─────────────────────────┘
                         │ EF Core
┌────────────────────────▼────────────────────────┐
│  Data access   — PingdbContext (database-first)  │
└────────────────────────┬────────────────────────┘
                         │
┌────────────────────────▼────────────────────────┐
│  Storage  — MySQL  +  /wwwroot/uploads (files)   │
└──────────────────────────────────────────────────┘
```

The database has four tables: `user`, `chat`, `message`, and `message_reaction` (InnoDB, `utf8mb4`).

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [MySQL Server 8.0+](https://dev.mysql.com/downloads/) (e.g. via XAMPP / phpMyAdmin)
- A [Giphy API key](https://developers.giphy.com/) (free) for GIF search

### 1. Clone the repository

```bash
git clone https://github.com/<your-username>/Ping.git
cd Ping
```

### 2. Set up the database

Create the database and import the schema dump:

```bash
mysql -u root -p -e "CREATE DATABASE pingdb CHARACTER SET utf8mb4;"
mysql -u root -p pingdb < pingdb.sql
```

### 3. Configure the connection string

Update the connection string in `appsettings.json` (or in `PingdbContext.OnConfiguring`) to match your MySQL setup:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3306;database=pingdb;user=root;password=YOUR_PASSWORD"
  }
}
```

### 4. Add your Giphy API key

Set your Giphy key in `wwwroot/js/media-picker.js` (the `GIPHY_API_KEY` constant).

### 5. Run

```bash
dotnet restore
dotnet run
```

The app will be available at `https://localhost:5001` (or the port shown in the console).

---

## 📁 Project Structure

```
Ping/
├── Controllers/          # AccountController, HomeController, MessagesController, SettingsController
├── Hubs/                 # ChatHub.cs — SignalR real-time hub
├── Models/               # User, Chat, Message, MessageReaction, PingdbContext, view models
├── Helpers/              # AvatarHelper, HttpRequestExtensions
├── Views/                # Razor views (.cshtml) and partials
├── wwwroot/
│   ├── js/               # chat.js, voice.js, image.js, media-picker.js, phone-input.js, site.js
│   ├── css/              # site.css, chat.css, components.css, auth.css
│   └── uploads/          # avatars, voice, images (runtime)
├── pingdb.sql            # Database schema + dump
├── appsettings.json
└── Program.cs            # Entry point, DI, middleware, hub mapping
```

---

## 🧪 Testing

The solution includes a test project (`Ping.Tests`, xUnit) covering four levels:

- **Unit** — isolated logic (BCrypt hashing, avatar color, content-type detection, validation)
- **Integration** — controllers/hub against an EF Core in-memory database and the file system
- **System** — end-to-end security, access control, and integrity constraints
- **Validation** — user-facing acceptance of functional requirements

> **Note:** tests use the EF Core in-memory provider. Make sure `PingdbContext.OnConfiguring` is guarded with `if (!optionsBuilder.IsConfigured)` so the in-memory database isn't overridden by the MySQL configuration.

Run all tests:

```bash
dotnet test
```

---

## 🗺️ Roadmap

- [ ] Group chats
- [ ] End-to-end encryption
- [ ] Push notifications
- [ ] Mobile client
- [ ] Authorization on the SignalR hub (`RequireAuthorization`)

---

## 📄 License

This project was developed as a bachelor's qualification project. Feel free to use it for learning purposes.

---

<p align="center">Built with ASP.NET Core &amp; SignalR</p>
