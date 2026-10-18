USE PosDesktop;
GO
INSERT dbo.Categories (Name)
SELECT v.Name FROM (VALUES (N'Makanan'),(N'Minuman'),(N'Snack')) v(Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Categories c WHERE c.Name=v.Name);
GO
