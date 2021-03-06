﻿using System.Collections.Generic;
using System.Drawing;
using BattleBall.AStar.Algorithm;
using System.Linq;
using BattleBall.Core.GameClients;
using BattleBall.Communication.Protocol;
using BattleBall.Communication.Outgoing.Rooms;
using BattleBall.Core.Rooms.Pathfinding;
using BattleBall.Core.Items;
using BattleBall.Core.Rooms.Items;
using System;

namespace BattleBall.Core.Rooms
{
    class Room
    {
        #region Fields
        public MapModel Model { get; }
        public Dictionary<int, RoomUser> Players;
        internal RoomItemManager RoomItemManager;
        internal GameMap GameMap;

        #endregion

        #region Constructor
        public Room(MapModel model)
        {
            this.Model = model;
            this.Players = new Dictionary<int, RoomUser>();
            this.RoomItemManager = new RoomItemManager(this);
            this.GameMap = new GameMap(this, Model);
            GameMap.GenerateMaps();
        }
        #endregion

        #region Methods
        internal void AddPlayerToRoom(GameClient Session)
        {
            RoomUser User = new RoomUser(Session.User.Id, Model.DoorX, Model.DoorY, Model.DoorZ + 0.1, Model.DoorRot, Session.User, this);
            Session.User.CurrentRoom = this;

            SendMessage(new SerializeRoomUserComposer(User)); //Send new room user data to room
            lock (Players)
            {
                Players.Add(User.UserId, User); //Add new room user to users list
            }
            lock (RoomItemManager.RoomItems)
            {
                Session.SendMessage(new SerializeRoomItemComposer(RoomItemManager.RoomItems.Values.ToList())); //Send all furni in room to user
            }
            lock (RoomItemManager.WallItems)
            {
                Session.SendMessage(new SerializeWallItemComposer(RoomItemManager.WallItems.Values.ToList())); //Send all furni in room to user
            }
            lock (Players)
            {
                Session.SendMessage(new SerializeRoomUserComposer(Players.Values.ToList())); //Send all players in room to user
                Session.SendMessage(SerializeStatusUpdates(Players.Values.ToList(), true));
            }
        }

        internal void SendModelToPlayer(GameClient Session)
        {
            Session.SendMessage(new MapComposer(Model)); //Send map data to player
        }

        internal void RemovePlayerFromRoom(GameClient session)
        {
            if (session == null)
                return;
            RoomUser user = GetRoomUserByUserId(session.User.Id);
            if (user == null)
            {
                return;
            }
            GameMap.Map[user.X, user.Y] = user.CurrentSqState;
            GameMap.RemoveUserFromMap(user, new Point(user.X, user.Y));
            user.User.CurrentRoom = null;
            lock (Players)
            {
                Players.Remove(session.User.Id);
            }

            SendMessage(new PlayerRemoveComposer(session.User.Id));
        }

        internal RoomUser GetRoomUserByName(string username)
        {
            List<RoomUser> currentPlayers;
            lock (Players)
            {
                currentPlayers = Players.Values.ToList();
            }
            foreach (RoomUser user in currentPlayers)
            {
                if (user.User.Username.ToLower() == username.ToLower())
                {
                    return user;
                }
            }
            return null;
        }

        internal RoomUser GetRoomUserByUserId(int id)
        {
            lock (Players)
            {
                if (Players.ContainsKey(id))
                {
                    return Players[id];
                }
            }            
            return null;
        }

        internal bool IsValidUser(RoomUser user)
        {
            return true;
        }

        internal ServerMessage SerializeStatusUpdates(List<RoomUser> users, bool all)
        {
            List<RoomUser> usersToUpdate = new List<RoomUser>();
            foreach (RoomUser user in users)
            {
                if (!all)
                {
                    if (!user.NeedsUpdate)
                        continue;
                    user.NeedsUpdate = false;
                }
                usersToUpdate.Add(user);
            }
            if (usersToUpdate.Count == 0)
                return null;

            return new SerializeRoomUserStatus(usersToUpdate);
        }

        internal void OnCycle()
        {
            List<RoomUser> currentPlayers;
            lock (Players)
            {
                currentPlayers = Players.Values.ToList();
            }          

            foreach (RoomUser player in currentPlayers)
            {
                if (player.SetStep)
                {
                    if (SetStepForUser(player))
                    {
                        continue;
                    }   
                }
                if (player.IsWalking)
                {
                    CalculatePath(player);
                }
                else
                {
                    player.RemoveStatus("mv");
                }
            }

            BroadcastStatusUpdates(currentPlayers);
        }

        private void BroadcastStatusUpdates(List<RoomUser> currentPlayers)
        {
            ServerMessage statusUpdates = SerializeStatusUpdates(currentPlayers, false);
            if (statusUpdates != null)
                SendMessage(statusUpdates);
        }

        public void UpdateUserStatusses()
        {
            List<RoomUser> players;
            lock (Players.Values)
            {
                players = Players.Values.ToList();
            }

            foreach (RoomUser user in players)
            {
                UpdateUserStatus(user);
            }
        }

        internal void UpdateUserStatus(RoomUser user)
        {
            if (user.Statusses.ContainsKey("lay") || user.Statusses.ContainsKey("sit"))
            {
                user.Statusses.Remove("lay");
                user.Statusses.Remove("sit");
                user.NeedsUpdate = true;
            }

            List<RoomItem> itemsOnSquare = GameMap.GetCoordinatedHeighestItems(user.X, user.Y);
            foreach (RoomItem item in itemsOnSquare)
            {
                item.OnUserWalk(user);
            }

        }

        private bool SetStepForUser(RoomUser player)
        {
            if (GameMap.CanWalk(player.SetX, player.SetY))
            {
                GameMap.UpdateUserMovement(new Point(player.X, player.Y), new Point(player.SetX, player.SetY), player);
                player.X = player.SetX;
                player.Y = player.SetY;
                player.Z = player.SetZ;

                UpdateUserStatus(player);
            }
            else
            {
                player.IsWalking = false;
                return true;
            }
            player.SetStep = false;
            return false;
        }

        private void CalculatePath(RoomUser player)
        {
            SquarePoint point = DreamPathfinder.GetNextStep(player.X, player.Y, player.TargetX, player.TargetY, GameMap.Map, GameMap.ItemHeightMap, Model.MaxX, Model.MaxY, false, GameMap.DiagonalEnabled);
            if (point.X == player.X && point.Y == player.Y) //No path found, or reached goal (:
            {
                player.IsWalking = false;
                player.RemoveStatus("mv");
            }
            else
            {
                HandleSetMovement(point, player);
            }
            player.NeedsUpdate = true;
        }

        internal void HandleSetMovement(SquarePoint point, RoomUser user)
        {
            int nextX = point.X;
            int nextY = point.Y;

            user.RemoveStatus("mv");
            double nextZ = GameMap.SqAbsoluteHeight(nextX, nextY);
            user.RemoveStatus("lay");
            user.RemoveStatus("sit");

            user.AddStatus("mv", nextX + "," + nextY + "," + Misc.TextHandling.GetString(nextZ));
            int newRot = GameMap.CalculateRotation(user.X, user.Y, nextX, nextY);

            user.Rot = newRot;

            user.SetStep = true;
            user.SetX = nextX;
            user.SetY = nextY;
            user.SetZ = nextZ;

            GameMap.Map[user.X, user.Y] = user.CurrentSqState;
            user.CurrentSqState = GameMap.Map[user.SetX, user.SetY];
            GameMap.Map[user.SetX, user.SetY] = SqState.Closed;


            user.NeedsUpdate = true;
        }

        internal bool ValidTile(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Model.MaxX && y < Model.MaxY && Model.Map[x, y] != 0;
        }

        internal void SendMessage(ServerMessage Message)
        {
            List<RoomUser> playersToSend;
            lock (Players.Values)
            {
                playersToSend = Players.Values.ToList();
            }

            foreach (RoomUser user in playersToSend)
            {
                if (user != null)
                {
                    user.User.Session.SendMessage(Message);
                }
            }
        }

        internal void OnPlayerWalksOffTile(RoomUser player, int x, int y)
        {
            
        }
        #endregion
    }
}
