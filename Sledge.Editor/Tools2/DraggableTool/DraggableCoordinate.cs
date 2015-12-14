using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Sledge.DataStructures.Geometric;
using Sledge.Editor.Extensions;
using Sledge.Editor.Rendering;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Scenes;
using Sledge.Rendering.Scenes.Elements;

namespace Sledge.Editor.Tools2.DraggableTool
{
    public class DraggableCoordinate : BaseDraggable
    {
        public bool Highlighted { get; protected set; }
        public Coordinate Position { get; set; }

        public DraggableCoordinate()
        {
            Position = Coordinate.Zero;
        }

        public override void Click(MapViewport viewport, ViewportEvent e, Coordinate position)
        {
            
        }

        public override bool CanDrag(MapViewport viewport, ViewportEvent e, Coordinate position)
        {
            const int width = 5;
            var screenPosition = viewport.ProperWorldToScreen(Position);
            var diff = (e.Location - screenPosition).Absolute();
            return diff.X < width && diff.Y < width;
        }

        protected virtual void SetMoveCursor(MapViewport viewport)
        {
            viewport.Control.Cursor = Cursors.SizeAll;
        }

        public override void Highlight(MapViewport viewport)
        {
            Highlighted = true;
            SetMoveCursor(viewport);
        }

        public override void Unhighlight(MapViewport viewport)
        {
            Highlighted = false;
            viewport.Control.Cursor = Cursors.Default;
        }

        public override void Drag(MapViewport viewport, ViewportEvent e, Coordinate lastPosition, Coordinate position)
        {
            Position = viewport.Expand(position) + viewport.GetUnusedCoordinate(Position);
            base.Drag(viewport, e, lastPosition, position);
        }

        public override IEnumerable<SceneObject> GetSceneObjects()
        {
            yield break;
        }

        public override IEnumerable<Element> GetViewportElements(MapViewport viewport, PerspectiveCamera camera)
        {
            yield break;
        }

        public override IEnumerable<Element> GetViewportElements(MapViewport viewport, OrthographicCamera camera)
        {
            yield return new HandleElement(PositionType.World, HandleElement.HandleType.Square, new Position(Position.ToVector3()), 2)
            {
                Color = GetColor()
            };
        }

        protected virtual Color GetColor()
        {
            return Highlighted ? Color.Red : Color.Green;
        }
    }
}