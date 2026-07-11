using CustomerCommLib;
using Moq;
using NUnit.Framework;
namespace CustomerCommLib.Tests{
public class CustomerCommTests{
[Test]
public void SendMailToCustomer_ShouldReturnTrue(){
var mock=new Mock<IMailSender>();
mock.Setup(x=>x.SendMail(It.IsAny<string>(),It.IsAny<string>())).Returns(true);
var cc=new CustomerComm(mock.Object);
Assert.That(cc.SendMailToCustomer(),Is.True);
}
[Test]
public void SendMail_ShouldBeCalledExactlyOnce(){
var mock=new Mock<IMailSender>();
mock.Setup(x=>x.SendMail(It.IsAny<string>(),It.IsAny<string>())).Returns(true);
var cc=new CustomerComm(mock.Object);
cc.SendMailToCustomer();
mock.Verify(x=>x.SendMail(It.IsAny<string>(),It.IsAny<string>()),Times.Once);
}}}