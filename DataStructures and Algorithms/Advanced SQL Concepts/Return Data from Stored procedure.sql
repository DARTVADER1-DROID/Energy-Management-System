CREATE PROCEDURE sp_GetEmployeeCountByDepartment
    @DepartmentID INT
AS
BEGIN
    SELECT COUNT(*) AS TotalEmployees
    FROM Employees
    WHERE DepartmentID = @DepartmentID;
END;

EXEC sp_GetEmployeeCountByDepartment @DepartmentID = 3;



EXEC sp_InsertEmployee
    @EmployeeID = 5,
    @FirstName = 'Robert',
    @LastName = 'Brown',
    @DepartmentID = 3,
    @Salary = 6500.00,
    @JoinDate = '2024-02-15';

EXEC sp_GetEmployeeCountByDepartment @DepartmentID = 3;