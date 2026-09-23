using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Serilog;
using Serilog.Core;

namespace WCBathAutomation.ViewModels;

public sealed partial class WCBathAutomationViewModel : ObservableObject
{
    private Document _doc = RevitContext.ActiveDocument;

    public WCBathAutomationViewModel()
    {
        if (_doc == null)
        {
            Log.Information("doc is null");
        }
    }

    [RelayCommand]
    public void handleShowInfo()
    {
        var roomsbath = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Where(room => room.Name.Contains("Bath", StringComparison.OrdinalIgnoreCase)).Cast<Room>();

        foreach (var room in roomsbath)
        {
            var level = room.Level;
            var parentView = GetFloorPlanViewByLevel(_doc, level);
            if (level == null || parentView == null)
            {
                throw new Exception("level or  parentView is null");
            }
            CreateCallOutForRoomInFloor(_doc,parentView,  room, 3); //3feet
            CreateVerticalSectionCallouts(_doc, room, 1); //1feet margin

            TaskDialog.Show("Thông báo", "Create callouts success");
        }
    }

    /// <summary>
    /// Tạo 4 mặt cắt đứng (Bắc/Nam/Đông/Tây) bao trọn phòng, dùng cho hồ sơ WC.
    /// </summary>
    private void CreateVerticalSectionCallouts(Document doc, Room room, double marginFeet)
    {
        BoundingBoxXYZ roomBox = room.get_BoundingBox(null);
        if (roomBox == null)
        {
            Log.Warning($"Room {room.Name} {room.Number} không có bounding box, bỏ qua mặt cắt đứng");
            return;
        }

        XYZ center = (roomBox.Min + roomBox.Max) / 2;
        double widthX = roomBox.Max.X - roomBox.Min.X;
        double widthY = roomBox.Max.Y - roomBox.Min.Y;
        double baseZ = roomBox.Min.Z;
        double height = roomBox.Max.Z - roomBox.Min.Z;

        var directions = new (string TenHuong, XYZ LookDir, XYZ Origin, double CutLength, double Depth)[]
        {
            ("Bac", XYZ.BasisY, new XYZ(center.X, roomBox.Min.Y, baseZ), widthX, widthY),
            ("Nam", -XYZ.BasisY, new XYZ(center.X, roomBox.Max.Y, baseZ), widthX, widthY),
            ("Dong", XYZ.BasisX, new XYZ(roomBox.Min.X, center.Y, baseZ), widthY, widthX),
            ("Tay", -XYZ.BasisX, new XYZ(roomBox.Max.X, center.Y, baseZ), widthY, widthX),
        };

        ElementId sectionTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.ViewTypeSection);

        using (Transaction t = new Transaction(doc, "MatCatDungWC"))
        {
            try
            {
                t.Start();

                foreach (var dir in directions)
                {
                    Transform sectionTransform = Transform.Identity;
                    sectionTransform.Origin = dir.Origin;
                    sectionTransform.BasisY = XYZ.BasisZ;
                    sectionTransform.BasisZ = dir.LookDir;
                    sectionTransform.BasisX = XYZ.BasisZ.CrossProduct(dir.LookDir);

                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
                    {
                        Transform = sectionTransform,
                        Min = new XYZ(-dir.CutLength / 2 - marginFeet, 0, -marginFeet),
                        Max = new XYZ(dir.CutLength / 2 + marginFeet, height + marginFeet, dir.Depth + marginFeet)
                    };

                    ViewSection sectionView = ViewSection.CreateSection(doc, sectionTypeId, sectionBox);
                    sectionView.Scale = 20;
                    sectionView.DetailLevel = ViewDetailLevel.Fine;
                    sectionView.Name = $"Mặt cắt WC - {room.Number} - {room.Name} - {dir.TenHuong}";

                    Log.Information($"Tạo thành công mặt cắt đứng: {sectionView.Name}");
                }

                t.Commit();
            }
            catch (Exception e)
            {
                t.RollBack();
                Log.Error(e, $"Lỗi khi tạo mặt cắt đứng cho room {room.Name} {room.Number}");
                throw;
            }
        }
    }

    private void CreateCallOutForRoomInFloor(Document doc, View parentView,  Room room, Double offsetToCallOut)
    {
        //Lấy boundingbox
        BoundingBoxXYZ boundingBox = room.get_BoundingBox(parentView);
        XYZ pt1 = new XYZ(boundingBox.Min.X - offsetToCallOut, boundingBox.Min.Y - offsetToCallOut, boundingBox.Min.Z );
        XYZ pt2 = new XYZ(boundingBox.Max.X + offsetToCallOut, boundingBox.Max.Y + offsetToCallOut, boundingBox.Max.Z );
        
        // 3. Lấy ViewTypeId của Callout (thường là Floor Plan Callout)
        ElementId calloutTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.ViewTypeFloorPlan);
        using (Transaction t = new Transaction(doc, "CallOutForRoom"))
        {
            try
            {
                t.Start();
                var calloutView = ViewSection.CreateCallout(_doc, parentView.Id, calloutTypeId, pt1, pt2);
                
                calloutView.Scale = 20;

                // 2. Chuyển độ chi tiết sang Fine (Hiển thị chi tiết lớp tường, thiết bị)
                calloutView.DetailLevel = ViewDetailLevel.Fine;

                // 3. Bật hiển thị Crop Box và bật tính năng cắt View
                calloutView.CropBoxActive = true;
                calloutView.CropBoxVisible = true;
                
                
                // Đổi tên Callout View theo mã phòng
                calloutView.Name = "Chi tiết WC - " + room.Number + " - " + room.Name;
                t.Commit();
                
                Log.Information($"Tạo thành công callout: {calloutView.Name} cho {room.Name} ở level {room.Level} ");
            }
            catch (Exception e)
            {
                t.RollBack();
                Console.WriteLine(e);
                throw;
            }
        }
    }

    public View? GetFloorPlanViewByLevel(Document doc, Level roomLevel)
    {
        // 1. Quét toàn bộ các đối tượng thuộc Class ViewPlan trong file Revit
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            // 2. Lọc ra View mặt bằng chuẩn
            .FirstOrDefault(v => !v.IsTemplate // Không lấy View Template
                                 && v.ViewType == ViewType.FloorPlan // Chỉ lấy mặt bằng kiến trúc (Floor Plan)
                                 && v.GenLevel != null // View đó phải gắn với một Level
                                 && v.GenLevel.Id == roomLevel.Id); // Level của View phải trùng với Level của Room
    }
}