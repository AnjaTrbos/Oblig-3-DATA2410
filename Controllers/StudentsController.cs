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
                while (await reader.ReadAsync())
                {
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
                using var updateCmd = new SqlCommand(
                    "UPDATE Students SET Grade = @Grade WHERE Id = @Id",
                    conn);
                updateCmd.Parameters.AddWithValue("@Id", student.Id);
                updateCmd.Parameters.AddWithValue("@Grade", student.Grade ?? (object)DBNull.Value);
                await updateCmd.ExecuteNonQueryAsync();
            }

            return studentsWithGrade;
        }
        catch (SqlException ex)
        {
            return Problem($"Database error while calculating grades: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Problem($"Unexpected error while calculating grades: {ex.Message}");
        }
    }

    [HttpGet("report")]
    public async Task<IActionResult> Report()
    {
        // Write code for the report generation logic.
        return Ok();
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
