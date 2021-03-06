﻿using System;
using System.Collections.Generic;
using System.Drawing;
using BattleBall.Communication.Outgoing.Rooms;
using BattleBall.Communication.Protocol;
using BattleBall.Core.Items;
using BattleBall.Misc;

namespace BattleBall.Core.Rooms.Items
{
    class RoomItem
    {
        internal int ItemId;
        internal int X, Y, Rot;
        internal double Z;
        internal int State;
        public Room Room { get; }

        public bool NeedsUpdate { get; set; }
        internal BaseItem BaseItem;
        internal RoomItemInteractor Interactor;
        private List<Point> coords;
        
        public double TotalHeight
        {
            get
            {
                return BaseItem.Z + Z;
            }
        }

        public RoomItem(int itemId, int x, int y, double z, int rot, int state, Room room, BaseItem baseItem)
        {
            ItemId = itemId;
            X = x;
            Y = y;
            Rot = rot;
            Z = z;
            Room = room;
            BaseItem = baseItem;
            State = state;
            NeedsUpdate = false;
            if (BaseItem.States > 1)
                Interactor = new InteractorGeneric(this);
            else
                Interactor = new InteractorNone(this);
        }

        internal List<Point> Coords
        {
            get
            {
                if (NeedsUpdate || coords == null)
                {
                    coords = GameMap.GetAffectedTiles(BaseItem.X, BaseItem.Y, X, Y, Rot);
                    coords.Add(new Point(X, Y));
                }
                return coords;
            }
        }

        internal void UpdateState()
        {
            NeedsUpdate = true;
            ServerMessage updateMessage = new FurniStateComposer(ItemId, State);
            Room.SendMessage(updateMessage);
        }

        internal void OnUserWalk(RoomUser user)
        {
            if (BaseItem.IsSeat)
            {
                user.AddStatus("sit", TextHandling.GetString(BaseItem.Z));
                user.Z = Z;
                user.Rot = Rot;

                user.NeedsUpdate = true;
            }
        }
    }
}
