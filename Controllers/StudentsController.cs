using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using data2410_api_v1.Models;

namespace data2410_api_v1.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentsController(IConfiguration config) : ControllerBase
{
    private readonly string _connectionString = config.GetConnectionString("DefaultConnection")!;

    private static string GetGrade(int marks) => marks switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 60 => "C",
        _ => "D"
    };

    [HttpGet]
    public async Task<ActionResult<List<Student>>> GetAll()
    {
        var students = new List<Student>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand("SELECT Id, Name, Course, Marks, Grade FROM Students", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            students.Add(new Student
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Course = reader.GetString(2),
                Marks = reader.GetInt32(3),
                Grade = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return students;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Student>> GetById(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand("SELECT Id, Name, Course, Marks, Grade FROM Students WHERE Id = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return NotFound();

        return new Student
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Course = reader.GetString(2),
            Marks = reader.GetInt32(3),
            Grade = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }

    [HttpPost]
    public async Task<ActionResult<Student>> Create(Student student)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(
            "INSERT INTO Students (Name, Course, Marks) OUTPUT INSERTED.Id VALUES (@Name, @Course, @Marks)", conn);
        cmd.Parameters.AddWithValue("@Name", student.Name);
        cmd.Parameters.AddWithValue("@Course", student.Course);
        cmd.Parameters.AddWithValue("@Marks", student.Marks);

        student.Id = (int)await cmd.ExecuteScalarAsync();
        return CreatedAtAction(nameof(GetById), new { id = student.Id }, student);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Student updated)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(
            "UPDATE Students SET Name = @Name, Course = @Course, Marks = @Marks WHERE Id = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Name", updated.Name);
        cmd.Parameters.AddWithValue("@Course", updated.Course);
        cmd.Parameters.AddWithValue("@Marks", updated.Marks);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpPost("calculate-grades")]
    public async Task<ActionResult<List<Student>>> CalculateGrades()
    {
        var studentsWithGrade = new List<Student>();

        try
        { // first try to connect and then read all the students from the database
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            

            // 1) Read all students by using a simple sql select query 
            using (var readCmd = new SqlCommand("SELECT Id, Name, Course, Marks FROM Students", conn))
            // getting the results by using a data reader and then calculating the grade for each student and storing it in a list of students with grade
            using (var reader = await readCmd.ExecuteReaderAsync())
            {
                // Read each student and calculate their grade
                while (await reader.ReadAsync())
                {
                    // use 3 as the index for marks because it is the 4th column in the select query (Id, Name, Course, Marks)
                    var marks = reader.GetInt32(3);
                    studentsWithGrade.Add(new Student
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Course = reader.GetString(2),
                        Marks = marks,
                        Grade = GetGrade(marks)
                    });
                }
            }

            // 2) Calculate + update grade for each student
            foreach (var student in studentsWithGrade)
            {
                // Update the grade for each student in the database using a simple sql set query
                using var updateCmd = new SqlCommand(
                    "UPDATE Students SET Grade = @Grade WHERE Id = @Id",
                    conn);
                updateCmd.Parameters.AddWithValue("@Id", student.Id); // which student to use
                updateCmd.Parameters.AddWithValue("@Grade", student.Grade ?? (object)DBNull.Value); // which grade to set
                await updateCmd.ExecuteNonQueryAsync(); // execute the update query for each student
            }

            return studentsWithGrade;
        }
        catch (SqlException ex) // catch any sql exceptions that may occur during the database operations
        {
            return Problem($"Database error while calculating grades: {ex.Message}");
        }
        catch (Exception ex) // catch any other exceptions that may occur during the grade calculation process
        {
            return Problem($"Unexpected error while calculating grades: {ex.Message}");
        }
    }

    [HttpGet("report")]
    public async Task<IActionResult> Report()
    {
        try
        {
            // Connect to the database
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // with the help of groupby command we can group the students by couruse 
            using var cmd = new SqlCommand(
                @"SELECT
                    Course,
                    COUNT(*) AS StudentCount, // count all students in each course
                    AVG(Marks) AS AverageMarks, // calculate the average marks for each course
                    SUM(CASE WHEN Grade = 'A' THEN 1 ELSE 0 END) AS GradeACount, // count how many students got grade A in each course
                    SUM(CASE WHEN Grade = 'B' THEN 1 ELSE 0 END) AS GradeBCount, // same as above 
                    SUM(CASE WHEN Grade = 'C' THEN 1 ELSE 0 END) AS GradeCCount,
                    SUM(CASE WHEN Grade = 'D' THEN 1 ELSE 0 END) AS GradeDCount
                  FROM Students
                  GROUP BY Course", conn);
            using var reader = await cmd.ExecuteReaderAsync();

            // read the results and store in a lis of objects 
            var report = new List<object>();
            while (await reader.ReadAsync())
            {
                // for each course we will have the course name, student count, average marks and the count of each grade in that course
                report.Add(new
                {
                    Course = reader.GetString(0),
                    StudentCount = reader.GetInt32(1),
                    AverageMarks = reader.GetDecimal(2),
                    GradeACount = reader.GetInt32(3),
                    GradeBCount = reader.GetInt32(4),
                    GradeCCount = reader.GetInt32(5),
                    GradeDCount = reader.GetInt32(6)
                });
            }

            return Ok(report); // return the report 
        }
        catch (SqlException ex) // catch any sql exceptions that may occur during the database operations
        {
            return Problem($"Database error while generating report: {ex.Message}");
        }
        catch (Exception ex) // catch any other exceptions that may occur during the report generation process
        {
            return Problem($"Unexpected error while generating report: {ex.Message}");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand("DELETE FROM Students WHERE Id = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", id);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows == 0 ? NotFound() : NoContent();
    }
}
