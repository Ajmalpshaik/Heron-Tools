// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - the room the eye is in, for the location line ("Level 03  -  Room 3.12 Plant Room") and each snag's
// Room column. No rays: Revit's own GetRoomAtPoint, at most four times a second and only after a 300 mm move (or
// when the world is refreshed). This model's rooms first, in the walk view's phase; when there is none, the rooms
// of a linked model whose box holds the point - read in the link's own coordinates and in its last phase (a
// linked model's phases cannot be matched to this view's). NEEDS REAL REVIT: rooms in links, and the phase.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace HeronTools.Services.ModelWalk
{
    internal sealed partial class WalkEngine
    {
        private const int MaxLinksForRooms = 4;   // a federated model with many links: only the first few that hold the point

        private sealed class RoomLink
        {
            public Document LinkDoc;
            public Transform ToLink;
            public BoundingBoxXYZ Box;
            public string Name;
        }

        private XYZ _lastRoomPoint;
        private double _lastRoomCheck = -10;
        private bool _roomSourcesDirty = true;
        private Phase _viewPhase;
        private readonly List<RoomLink> _roomLinks = new List<RoomLink>();

        private void UpdateRoom(Document doc, View3D view, XYZ position, double now, WalkHudState hud)
        {
            if (now - _lastRoomCheck < WalkTuning.ClearHeightRefreshSec)
                return;
            // Standing on the floor, 300 mm up: inside the room's height even with a low upper limit.
            XYZ probe = new XYZ(position.X, position.Y, position.Z - _session.EyeHeightFt + WalkTuning.MmToFt(300));
            if (!_roomSourcesDirty && _lastRoomPoint != null && _lastRoomPoint.DistanceTo(probe) < WalkTuning.MmToFt(300))
                return;
            _lastRoomCheck = now;
            _lastRoomPoint = probe;

            if (_roomSourcesDirty)
                ReadRoomSources(doc, view);

            string text = "";
            Room room = RoomAt(doc, probe, _viewPhase);
            if (room != null)
            {
                text = RoomText(room);
            }
            else
            {
                int tried = 0;
                foreach (RoomLink link in _roomLinks)
                {
                    if (tried >= MaxLinksForRooms)
                        break;
                    if (link.Box != null && !Inside(link.Box, probe))
                        continue;
                    tried++;
                    Room linked = RoomAt(link.LinkDoc, link.ToLink.OfPoint(probe), null);
                    if (linked != null)
                    {
                        text = RoomText(linked) + " (" + link.Name + ")";
                        break;
                    }
                }
            }
            hud.RoomText = text;
        }

        private void ReadRoomSources(Document doc, View3D view)
        {
            _roomSourcesDirty = false;
            _viewPhase = null;
            try
            {
                Parameter phase = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (phase != null && phase.HasValue && phase.StorageType == StorageType.ElementId)
                    _viewPhase = doc.GetElement(phase.AsElementId()) as Phase;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { _viewPhase = null; /* last phase is used */ }

            _roomLinks.Clear();
            foreach (Element e in new FilteredElementCollector(doc, view.Id).OfClass(typeof(RevitLinkInstance)))
            {
                try
                {
                    var link = (RevitLinkInstance)e;
                    Document linkDoc = link.GetLinkDocument();
                    if (linkDoc == null)
                        continue;   // not loaded
                    _roomLinks.Add(new RoomLink
                    {
                        LinkDoc = linkDoc,
                        ToLink = link.GetTotalTransform().Inverse,
                        Box = link.get_BoundingBox(null),
                        Name = WalkCollision.LinkName(link)
                    });
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* a link Revit cannot read gives no room */ }
            }
        }

        private static Room RoomAt(Document doc, XYZ point, Phase phase)
        {
            try
            {
                return phase != null ? doc.GetRoomAtPoint(point, phase) : doc.GetRoomAtPoint(point);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;   // a bonus line - never fail a frame for it
            }
        }

        /// <summary>"Room 3.12 Plant Room" - only the parts the room has.</summary>
        private static string RoomText(Room room)
        {
            string number = (room.Number ?? "").Trim();
            string name = "";
            try { name = (room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "").Trim(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { name = ""; }
            string both = (number + " " + name).Trim();
            return both.Length == 0 ? "Room" : "Room " + both;
        }

        private static bool Inside(BoundingBoxXYZ b, XYZ p) =>
            p.X >= b.Min.X && p.X <= b.Max.X && p.Y >= b.Min.Y && p.Y <= b.Max.Y && p.Z >= b.Min.Z && p.Z <= b.Max.Z;
    }
}
