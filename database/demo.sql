USE PosDesktop;
GO
INSERT dbo.Products (Name, CategoryId, Price, IsActive)
SELECT v.Name, c.Id, v.Price, 1
FROM (VALUES (N'Nasi Goreng',N'Makanan',25000.00),
             (N'Es Teh',N'Minuman',8000.00),
             (N'Keripik',N'Snack',12000.00)) v(Name,CategoryName,Price)
JOIN dbo.Categories c ON c.Name=v.CategoryName
WHERE NOT EXISTS (SELECT 1 FROM dbo.Products p WHERE p.Name=v.Name AND p.CategoryId=c.Id);
GO
