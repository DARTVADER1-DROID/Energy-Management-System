using System.Net;
using System.Net.Mail;
namespace CustomerCommLib{
public class MailSender:IMailSender{
public bool SendMail(string toAddress,string message){
MailMessage mail=new();
SmtpClient smtp=new("smtp.gmail.com");
mail.From=new MailAddress("your_email@gmail.com");
mail.To.Add(toAddress);
mail.Subject="Test Mail";
mail.Body=message;
smtp.Port=587;
smtp.Credentials=new NetworkCredential("username","password");
smtp.EnableSsl=true;
smtp.Send(mail);
return true;}}}