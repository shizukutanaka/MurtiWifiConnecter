// 製品ビルドでは <ImplicitUsings>enable</ImplicitUsings> が下記を自動で入れる。
// csc を直接叩くときは入らないため、型検査ハーネス用にここで補う。
// (コンソールアプリの既定セット。これが無いと `Console` すら解決できず、
//  実際の欠陥と区別がつかない偽エラーが出る。)
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
