using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EllieMae.EMLite.ClientServer;
using EllieMae.EMLite.RemotingServices;
using EllieMae.Encompass.BusinessObjects.Loans;
using EllieMae.Encompass.BusinessObjects.Loans.Logging;
using EllieMae.Encompass.Collections;
using Impac.Encompass.Exceptions;
using Impac.Encompass.Kit.API.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Text;
using System.Xml;

namespace Impac.Encompass.Kit.API.Services
{
    public class DataService : IDataService
    {
        public List<CreditContentModel> GetCreditContents(EllieMae.Encompass.Client.Session session, Loan loan)
        {
            List<CreditContentModel> creditContents = new List<CreditContentModel>();

            var _serverloanObj = GetLoanDataMgr(session, loan.Guid).LoanObject;
            string[] listofKeys = _serverloanObj.GetSupportingDataKeysOnCIFs().Where(k => k.ToUpper().Contains("LIABILITY")).ToArray();
            for (int borr = 0; borr < loan.BorrowerPairs.Count; borr++)
            {
                BorrowerPair bp = loan.BorrowerPairs[borr];
                //Pull CreditResponse XML
                if (listofKeys.Count() > 0)
                {
                    //Get Credit key by borrowerId
                    string key = listofKeys.Where(k => k.Contains(bp.Borrower.ID)).FirstOrDefault();
                    if (!string.IsNullOrEmpty(key))
                    {
                        BinaryObject binaryObject = _serverloanObj.GetSupportingDataOnCIFs(key);
                        string creditResponse = binaryObject.ToString().Replace("<?xml version=\"1.0\"?>", "");

                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(creditResponse);

                        //Removing EMBEDDED_FILE
                        XmlNode node = doc.SelectSingleNode("/RESPONSE_GROUP/RESPONSE/RESPONSE_DATA/CREDIT_RESPONSE/EMBEDDED_FILE");
                        if (node != null)
                            node.RemoveAll();
                        creditResponse = doc.OuterXml;

                        creditContents.Add(new CreditContentModel { Content = creditResponse });
                    }
                }
            }
            return creditContents;
        }

        private EllieMae.EMLite.DataEngine.LoanDataMgr GetLoanDataMgr(EllieMae.Encompass.Client.Session session, string loanGuidID)
        {
            MethodInfo dynMethod = session.GetType().GetMethod("Unwrap", BindingFlags.NonPublic | BindingFlags.Instance);
            MarshalByRefObject sessionobj = dynMethod.Invoke(session, null) as MarshalByRefObject;
            ObjRef objRef = RemotingServices.GetObjRefForProxy(sessionobj);
            //EllieMae.EMLite.Server.Session serverSession = (EllieMae.EMLite.Server.Session)RemotingServices.Unmarshal(objRef);
            Elli.Server.Remoting.SessionObjects.Session serverSession = (Elli.Server.Remoting.SessionObjects.Session)RemotingServices.Unmarshal(objRef);
            //Initialize ClientServer session object with serverSession
            SessionObjects sessionObjects = new SessionObjects(serverSession);

            //Get LoanDataMgr by client server session object and loan GUID 
            if (sessionObjects == null)
            {
                return null;
            }

            EllieMae.EMLite.DataEngine.LoanDataMgr loanDataMgr;
            try
            {
                loanDataMgr = EllieMae.EMLite.DataEngine.LoanDataMgr.OpenLoan(sessionObjects, loanGuidID, false);
            }
            catch (Exception ex)
            {
                //Log exception
                return null;
            }

            return loanDataMgr;
        }

        public bool UploadFNMFile(Guid loanGuid, string filePath, string encompassInstance, string encompassUserName, string encompassPassword)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException();

            using (EllieMae.Encompass.Client.Session session = new EllieMae.Encompass.Client.Session())
            {
                session.Start(encompassInstance, encompassUserName, encompassPassword);
                Loan loan;

                try
                {
                    loan = session.Loans.Open("{" + loanGuid.ToString() + "}", true, true);// "{bd019591-41b1-4fa5-8c97-0554f483413c}"; 
                }
                catch (Exception ex)
                {
                    throw new LoanLockedException(ex.Message);
                }

                if (loan == null) throw new FileNotFoundException();

                loan.Lock();
                loan.Import(filePath, LoanImportFormat.FNMA3X);
                loan.Commit();
                loan.Unlock();
                loan.Close();
                session.End();
            }

            return true;
        }

        public static void AddAttachment(byte[] file, string title, string fileExtension, Loan ln = null)
        {
            AddAttachment(file, title, fileExtension, title, ln);
        }

        public static void AddAttachment(byte[] file, string title, string fileExtension, string name, Loan ln)
        {
            try
            {

                EllieMae.Encompass.BusinessObjects.DataObject dataObject = new EllieMae.Encompass.BusinessObjects.DataObject(file);

                // Create a new attachment by importing it from a TIFF document on disk
                Attachment att = ln.Attachments.AddObject(dataObject, fileExtension);
                att.Title = name;

                LogEntryList documents = ln.Log.TrackedDocuments.GetDocumentsByTitle(title);

                if (documents.Count == 0)
                {
                    //Creating title
                    ln.Log.TrackedDocuments.Add(title, ln.Log.MilestoneEvents.NextEvent.MilestoneName);
                    documents = ln.Log.TrackedDocuments.GetDocumentsByTitle(title);
                }

                // Create a new attachment by importing it from disk
                TrackedDocument document = (TrackedDocument)documents[0];
                document.Attach(att);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Exception occurred in [DataService][AddAttachment] {0}", ex.Message));
            }
        }


        public bool UploadIUSEligibilityData(string loanGuid, string htmlContent, string encompassInstance, string encompassUserName, string encompassPassword)
        {
            using (EllieMae.Encompass.Client.Session session = new EllieMae.Encompass.Client.Session())
            {
                session.Start(encompassInstance, encompassUserName, encompassPassword);
                Loan loan = null;

                try
                {
                    loan = session.Loans.Open("{" + loanGuid.ToString() + "}", true, true);// "{bd019591-41b1-4fa5-8c97-0554f483413c}"; 

                }
                catch (Exception ex)
                {
                    session.End();
                    throw new LoanLockedException(ex.Message);
                }

                if (loan == null) throw new FileNotFoundException();

                try
                {
                    loan.Lock();

                    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    // Got the below code from: $\Apps\Work\Encompass\Customizations\CodeBase\Impac Eligibility\Impac.Eligibility.Codebase\Impac.Eligibility.Codebase\WinForm\EligibilityResponseForm.cs
                    // private void SaveResponseHtml(string certHtmlDecision)
                    // AttachmentHelper.AddAttachment(Encoding.ASCII.GetBytes(certHtmlDecision), "Eligibility Certificate", ".html", _loan);
                    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    AddAttachment(Encoding.ASCII.GetBytes(htmlContent), "TPO IUS Eligibility Certificate", ".html", loan);
                    loan.Commit();
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }
                finally
                {
                    if (loan != null)
                    {
                        loan.Unlock();
                        loan.Close();
                    }

                    // end the encompass login session
                    session.End();
                }

            } // end using client.session - NOTE, i am guessing "using" call would just put this session object in the garabage collector. I don't think it will do session.End. So we have to call that in above.


            return true;
        }


    }
}
