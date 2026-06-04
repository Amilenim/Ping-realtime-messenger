using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace Ping.Models;

public partial class PingdbContext : DbContext
{
    public PingdbContext()
    {
    }

    public PingdbContext(DbContextOptions<PingdbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<MessageReaction> MessageReactions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseMySql("server=localhost;port=3306;database=pingdb;user=root;password=12345678", Microsoft.EntityFrameworkCore.ServerVersion.Parse("8.0.17-mysql"));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_0900_ai_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("chat");

            entity.HasIndex(e => e.FirstUser, "first_user");

            entity.HasIndex(e => e.SecondUser, "second_user");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.FirstUser)
                .HasColumnType("int(11)")
                .HasColumnName("first_user");
            entity.Property(e => e.SecondUser)
                .HasColumnType("int(11)")
                .HasColumnName("second_user");

            entity.HasOne(d => d.FirstUserNavigation).WithMany(p => p.ChatFirstUserNavigations)
                .HasForeignKey(d => d.FirstUser)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("chat_ibfk_1");

            entity.HasOne(d => d.SecondUserNavigation).WithMany(p => p.ChatSecondUserNavigations)
                .HasForeignKey(d => d.SecondUser)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("chat_ibfk_2");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("message");

            entity.HasIndex(e => e.ChatId, "chat_id");

            entity.HasIndex(e => e.SenderId, "sender_id");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.ChatId)
                .HasColumnType("int(11)")
                .HasColumnName("chat_id");
            entity.Property(e => e.Content)
                .HasColumnType("text")
                .HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.IsEdited).HasColumnName("is_edited");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.SenderId)
                .HasColumnType("int(11)")
                .HasColumnName("sender_id");

            entity.HasOne(d => d.Chat).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ChatId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("message_ibfk_1");

            entity.HasOne(d => d.Sender).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("message_ibfk_2");
        });

        modelBuilder.Entity<MessageReaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("message_reaction");

            entity.HasIndex(e => e.MessageId, "idx_message_id");

            entity.HasIndex(e => e.UserId, "idx_user_id");

            entity.HasIndex(e => new { e.MessageId, e.UserId, e.Emoji }, "uniq_msg_user_emoji").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Emoji)
                .HasMaxLength(32)
                .HasColumnName("emoji");
            entity.Property(e => e.MessageId)
                .HasColumnType("int(11)")
                .HasColumnName("message_id");
            entity.Property(e => e.UserId)
                .HasColumnType("int(11)")
                .HasColumnName("user_id");

            entity.HasOne(d => d.Message).WithMany(p => p.MessageReactions)
                .HasForeignKey(d => d.MessageId)
                .HasConstraintName("fk_reaction_message");

            entity.HasOne(d => d.User).WithMany(p => p.MessageReactions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_reaction_user");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("user");

            entity.HasIndex(e => e.Username, "username").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.AvatarPath)
                .HasMaxLength(255)
                .HasColumnName("avatar_path");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Password)
                .HasMaxLength(128)
                .HasColumnName("password");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .HasColumnName("phone_number");
            entity.Property(e => e.Username)
                .HasMaxLength(32)
                .HasColumnName("username");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
