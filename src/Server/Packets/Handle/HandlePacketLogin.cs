﻿using Common.Networking.Packet;
using Common.Networking.IO;
using ENet;
using GameServer.Logging;
using GameServer.Utilities;
using System.Collections.Generic;
using System;
using System.Linq;
using Common.Game;

namespace GameServer.Server.Packets
{
    public class HandlePacketLogin : HandlePacket
    {
        public override ClientPacketOpcode Opcode { get; set; }

        public HandlePacketLogin() 
        {
            Opcode = ClientPacketOpcode.Login;
        }

        public override void Handle(Event netEvent, ref PacketReader packetReader)
        {
            var data = new RPacketLogin();
            data.Read(packetReader);

            var peer = netEvent.Peer;

            // Check JWT
            var token = new JsonWebToken(data.JsonWebToken);
            if (token.IsValid.Error != JsonWebToken.TokenValidateError.Ok)
            {
                ENetServer.Send(new ServerPacket((byte)ServerPacketOpcode.LoginResponse, new WPacketLogin
                {
                    LoginOpcode = LoginResponseOpcode.InvalidToken
                }), peer);
                return;
            }

            // Check if versions match
            if (data.VersionMajor != ENetServer.ServerVersion.Major || data.VersionMinor != ENetServer.ServerVersion.Minor || data.VersionPatch != ENetServer.ServerVersion.Patch)
            {
                var clientVersion = $"{data.VersionMajor}.{data.VersionMinor}.{data.VersionPatch}";
                var serverVersion = $"{ENetServer.ServerVersion.Major}.{ENetServer.ServerVersion.Minor}.{ENetServer.ServerVersion.Patch}";

                Logger.Log($"Player '{token.Payload.username}' tried to log in but failed because they are running on version " +
                    $"'{clientVersion}' but the server is on version '{serverVersion}'");

                ENetServer.Send(new ServerPacket((byte)ServerPacketOpcode.LoginResponse, new WPacketLogin
                {
                    LoginOpcode = LoginResponseOpcode.VersionMismatch,
                    ServerVersion = ENetServer.ServerVersion
                }), peer);

                return;
            }

            // Check if username exists in database
            var playerUsername = token.Payload.username;

            // Check if username is in the player banlist
            var bannedPlayers = FileManager.ReadConfig<List<BannedPlayer>>("banned_players");
            var bannedPlayer = bannedPlayers.Find(x => x.Name == playerUsername);

            if (bannedPlayer != null) 
            {
                // Player is banned, disconnect them immediately 
                netEvent.Peer.DisconnectNow((uint)DisconnectOpcode.Banned);
                Logger.Log($"Player '{bannedPlayer.Name}' tried to join but is banned");
                return;
            }

            // Check if a player with this username is logged in already
            foreach (var p in ENetServer.Players.Values)
            {
                if (p.InGame && p.Username.Equals(playerUsername)) 
                {
                    netEvent.Peer.DisconnectNow((uint)DisconnectOpcode.PlayerWithUsernameExistsOnServerAlready);
                    return;
                }
            }

            // These values will be sent to the client
            WPacketLogin packetData;

            var player = Player.GetPlayerConfig(playerUsername);

            if (player != null)
            {
                // RETURNING PLAYER

                player.Peer = netEvent.Peer;
                player.AddResourcesGeneratedFromStructures();

                packetData = new WPacketLogin
                {
                    LoginOpcode = LoginResponseOpcode.LoginSuccessReturningPlayer
                };

                player.InGame = true;

                // Add the player to the list of players currently on the server
                ENetServer.Players[peer.ID] = player;

                Logger.Log($"Player '{playerUsername}' logged in");
            }
            else
            {
                // NEW PLAYER
                packetData = new WPacketLogin
                {
                    LoginOpcode = LoginResponseOpcode.LoginSuccessNewPlayer
                };

                // Add the player to the list of players currently on the server
                ENetServer.Players[peer.ID] = new Player(peer) { Username = playerUsername, InGame = true };

                Logger.Log($"User '{playerUsername}' logged in for the first time");
            }

            ENetServer.Send(new ServerPacket((byte)ServerPacketOpcode.LoginResponse, packetData), peer);

            // Tell the joining client how many players are on the server
            //ENetServer.Send(new ServerPacket((byte)ServerPacketOpcode.PlayerList, new WPacketPlayerList()), peer);

            // Add the user to the global channel
            ENetServer.Channels[(uint)SpecialChannel.Global].Users.Add(peer.ID);
        }
    }
}
