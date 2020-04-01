using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Impac.Encompass.Kit.API.Services
{
    public interface IDataService
    {
        List<CreditContentModel> GetCreditContents(EllieMae.Encompass.Client.Session session, Loan loan);
        bool UploadFNMFile(Guid loanGuid, string filePath, string encompassInstance, string encompassUserName, string encompassPassword);
        bool UploadIUSEligibilityData(string loanGuid, string filePath, string encompassInstance, string encompassUserName, string encompassPassword);
    }
}
