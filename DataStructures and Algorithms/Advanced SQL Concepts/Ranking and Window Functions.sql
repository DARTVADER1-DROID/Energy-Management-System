SELECT *
FROM
(
    SELECT
        ProductID,
        ProductName,
        Category,
        Price,

        ROW_NUMBER() OVER (
            PARTITION BY Category
            ORDER BY Price DESC
        ) AS RowNum,

        RANK() OVER (
            PARTITION BY Category
            ORDER BY Price DESC
        ) AS ProductRank,

        DENSE_RANK() OVER (
            PARTITION BY Category
            ORDER BY Price DESC
        ) AS DenseRank

    FROM Products
) AS RankedProducts
WHERE RowNum <= 3
ORDER BY Category, RowNum;