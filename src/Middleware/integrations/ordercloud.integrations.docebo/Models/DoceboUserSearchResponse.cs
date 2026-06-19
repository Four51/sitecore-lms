using System;
using System.Collections.Generic;
using System.Text;

namespace ordercloud.integrations.docebo.Models
{

    public class DoceboUserSearchResponse
    {
        public DoceboUserSearchData data { get; set; }
        public string version { get; set; }
        public object[] _links { get; set; }
    }

    public class DoceboUserSearchData
    {
        public List<DoceboUserSearchItem> items { get; set; }
        public int count { get; set; }
        public bool has_more_data { get; set; }
        public object cursor { get; set; }
        public int current_page { get; set; }
        public int current_page_size { get; set; }
        public int total_page_count { get; set; }
        public int total_count { get; set; }
        public List<DoceboUserSearchSort> sort { get; set; }
    }

    public class DoceboUserSearchItem
    {
        public string user_id { get; set; }
        //public string username { get; set; }
        //public string first_name { get; set; }
        //public string last_name { get; set; }
        //public string email { get; set; }
        //public string uuid { get; set; }
        //public bool is_manager { get; set; }
        //public string fullname { get; set; }
        //public string last_access_date { get; set; }
        //public string last_update { get; set; }
        //public string creation_date { get; set; }
        //public string status { get; set; }
        //public string avatar { get; set; }
        //public string language { get; set; }
        //public string lang_code { get; set; }
        //public object expiration_date { get; set; }
        //public string level { get; set; }
        //public string email_validation_status { get; set; }
        //public string send_notification { get; set; }
        //public string newsletter_optout { get; set; }
        //public object newsletter_optout_date { get; set; }
        //public string encoded_username { get; set; }
        //public string timezone { get; set; }
        //public object date_format { get; set; }
        //public string field_4 { get; set; }
        //public string field_5 { get; set; }
        //public string field_7 { get; set; }
        //public string field_9 { get; set; }
        //public string field_10 { get; set; }
        //public string field_11 { get; set; }
        //public string field_13 { get; set; }
        //public string field_15 { get; set; }
        //public string field_16 { get; set; }
        //public string field_17 { get; set; }
        //public string field_18 { get; set; }
        //public string field_19 { get; set; }
        //public object field_22 { get; set; }
        //public string field_23 { get; set; }
        //public string field_24 { get; set; }
        //public string field_28 { get; set; }
        //public string field_29 { get; set; }
        //public string[] multidomains { get; set; }
        //public Manager_Names manager_names { get; set; }
        //public object[] managers { get; set; }
        //public int active_subordinates_count { get; set; }
        //public string[] actions { get; set; }
        //public bool expired { get; set; }
    }

    // Returned to Shopper-role callers via the public controller route.
    // Contains no PII — only indicates whether a matching Docebo account exists.
    public class DoceboUserExistsResponse
    {
        public bool exists { get; set; }
    }

    public class Manager_Names
    {
        public _1 _1 { get; set; }
    }

    public class _1
    {
        public string manager_title { get; set; }
        public object manager_name { get; set; }
        public object manager_username { get; set; }
    }

    public class DoceboUserSearchSort
    {
        public string sort_attr { get; set; }
        public string sort_dir { get; set; }
    }
}
