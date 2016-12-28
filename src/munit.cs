/**
 * munit:  a simple unit test framework.
 * 
 * munit was born out of frustraion with nunit in Xamarin.
 * With Nunit it was dificult to run the tests (at tiems they will not even start), 
 * harder (or even not available) to see the log when tests failed.
 * 
 * munit is simple. 
 * 
 * munit takes all test classes in an assemply and runs the test cases in them.
 * The results and failures with exeption stack trace are printed on the console.
 * 
 * A test class should have the following
 *      1. a no-arg public constructor
 *      2. a [TestClass] attribute
 *      3. onen or more public methods with [TestCase] attribute
 * 
 *      Additionally, a test class can have
 *      4. a static method with [OneTimeSetUp] attribute
 *      5. a static method with [OneTimeTearDown] attribute
 *      6. an instance method with [SetUp] attribute
 *      7. an instance method with [TearDown] attribute
 * 
 * 
 * Theory of operation
 * -------------------
 * munit loads the assembly 
 * reades all exported types in the assembly that are anotated with [TestClass].
 * Takes each test class T, 
 *   1. runs static [OneTimeSetUp] method, 
 *   2. for each [TestCase] method  m
 *      2.1 Create an instance t of T 
 *      2.2 runs [SetUp], if any, on t
 *      2.3 Runs m on t
 *      2.4 Runs [TearDown], if any, on t
 *   3. runs static [OneTimeTearDown]
 *  
 * 
 * Basic Steps
 * --------------
 * A1. Annotate a class with [TestClass]
 * A2. Annotate one or more methods with [TestCase] 
 * 
 * Optional,
 * A3. Annotate one static method with [OneTimeSetUp]     
 * A4. Annotate one static method with [OneTimeTearDown] 
 * A5. Annotate one instance method with [SetUp]           
 * A6. Annotate one instance method with [TearDown]        
 * 
 * B. Compile the test classes into a test.dll.
 *    
 * C. run munit on test.dll:
 * 
 * $ mono munit.exe /path/to/test.dll
 * 
 * 
 * Test Filtering
 * --------------
 * Sometimes only few test classes or few test cases of a class needs ti be run.
 * munit can filter tests
 * 
 * $ munit test.dll +TestClass
 * 
 * will run all test cases in TestClass
 * 
 * $ munit test.dll +TestClass.TestMethod
 * will run TestMethod in TestClass
 * 
 * 
 * Exception Test
 * ---------------
 * 
 * A test method can be marked with [ExpectedException(t)] where t is an Exception 
 * type. Then if the test method throws same type of exception, the test method
 * is considered to have passed; otherwise it would be a failed test.
 * 
 * [TestCase]
 * [ExpectedException(typeof(NotSupportedException)]
 * public void testDivideByZero() {
 *    // if this method throws NotSupportedException, 
 *    // then the test passes else it fails
 * }
 * 
 * 
 */
namespace munit
{
    using System;
    using System.Reflection;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using System.Diagnostics;
    using System.Runtime.Remoting.Messaging;
    using System.Linq;

    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestCaseAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class SetUpAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TearDownAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class OneTimeSetUpAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class OneTimeTearDownAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class ExpectedExceptionAttribute : Attribute
    {
        public ExpectedExceptionAttribute(Type e)
        {
            ExpectedExceptionType = e;
        }

        public Type ExpectedExceptionType { get; set; }
        public string Message { get; set;}
    }

    /** 
     * ---------------------------------------------------------------
     * Runs test classes
     * ----------------------------------------------------------------
     */
    public class TestRunner
    {
        /**
         * Loads an assempbly and inspects all exported types and if a type
         * is annotated with [TestClass] runs it.
         * 
         * @param args[0] Assembly containng the test classes
         */
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                printUsage();
                return;
            }

            TestReporter reporter = new TestReporter();
            for (int i = 0; i < args.Length; i++)
            {
                Assembly assembly = loadAssemblyFromPath(args[i]);
                if (assembly == null) continue;
                Regex testClassPattern = null;
                Regex testCasePattern = null;
                if (i < (args.Length - 1) && args[i + 1][0] == '+')
                {
                    i += 1;
                    string pattern = args[i].Substring(1);
                    int k = pattern.IndexOf('.');
                    if (k >= 0)
                    {
                        testClassPattern = new Regex(pattern.Substring(0, k));
                        testCasePattern = new Regex(pattern.Substring(k + 1));
                    }
                    else {
                        testClassPattern = new Regex(pattern);
                    }
                }
                foreach (Type type in assembly.ExportedTypes)
                {
                    try
                    {
                        if (testClassPattern != null &&
                            !testClassPattern.IsMatch(type.Name))
                        {
                            continue;
                        }


                        TestClass testClass = MakeTestClass(type);
                        if (testClass != null)
                        {
                            testClass.TestCasePattern = testCasePattern;
                            if (!testClass.Initialize()) continue;
                            Console.WriteLine(Environment.NewLine +
                                            "Running " + testClass.Type + " " +
                                            testClass.TestCount + " testcases");
                            testClass.RunTestCases(reporter);
                        }
                        else {
                            Console.WriteLine("Invalid test class " + type);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error running test class " + type);
                        TestReporter.printStackTrace(ex);
                    }
                }
                reporter.reportStats();
            }
        }

        private static void printUsage()
        {
            Console.WriteLine("runs test cases in test classes in an assembly"
                              + Environment.NewLine);
            Console.WriteLine("Usage: munit.exe (dll [+pattern])+");
            Console.WriteLine("where");
            Console.WriteLine("\tdll is full path name to an assembly of test classes");
            Console.WriteLine("\tpattern (optional) regular expression to filter test cases");
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine("Example:");
            Console.WriteLine("\t $ munit.exe ./path/to/test.dll");
            Console.WriteLine("\t or");
            Console.WriteLine("\t $ munit.exe ./path/to/test.dll +MyTestClass.myTestCaseMethod");
        }

        private static Assembly loadAssemblyFromPath(string assemblyPath)
        {
            try
            {
                Assembly assembly = Assembly.LoadFile(assemblyPath);
                return assembly;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Can not load input test assembly from " + assemblyPath);
                Console.WriteLine("Stack Trace below:");
                TestReporter.printStackTrace(ex);
            }
            return null;

        }



        /**
         * Creates a test class, if possible, after analyzing a given type.
         * Otherwise returns null.
         */
        public static TestClass MakeTestClass(Type t)
        {
            return t.GetCustomAttribute<TestClassAttribute>() == null
                ? null : new TestClass(t);
        }
    }

    /**
     * -------------------------------------------------------------------
     * A TestClass is build from a normal user class.
     * It analyzes method annotations to find methods to be called for
     * test executuon.
     * -------------------------------------------------------------------
     */
    public class TestClass
    {
        public Type Type { get; private set; }
        TestMethod OneTimeSetUp { get; set; }
        TestMethod OneTimeTearDown { get; set; }
        TestMethod SetUp { get; set; }
        TestMethod TearDown { get; set; }

        public Regex TestCasePattern { get; set; }

        public List<TestMethod> TestCases
        {
            get { return testCases; }
            set { testCases = value; }
        }

        public List<TestMethod> FrameworkTestMethods
        {
            get
            {
                List<TestMethod> result = new List<TestMethod>();
                if (OneTimeSetUp != null) result.Add(OneTimeSetUp);
                if (OneTimeTearDown != null) result.Add(OneTimeTearDown);
                if (SetUp != null) result.Add(SetUp);
                if (TearDown != null) result.Add(TearDown);
                return result;
            }
        }


        public int TestCount { get { return testCases.Count; } }

        private List<TestMethod> testCases = new List<TestMethod>();

        public event EventHandler TestResultEventHandler;


        public TestClass(Type t)
        {
            Type = t;

        }

        /**
         * Create a test class, analyze its method and constructor
         * to validate.
         */
        public bool Initialize()
        {
            bool valid = CreateTestInstance() != null;
            if (valid) scan();
            return valid && TestCases.Count > 0;
        }


        /**
         * Create an instance of TestClass.
         */
        object CreateTestInstance()
        {
            object testInstance = null;
            try
            {
                testInstance = Activator.CreateInstance(Type);
                return testInstance;
            }
            catch (Exception ex)
            {
                TestResultEventHandler(this, new TestResultEvent(
                    RESULT_TYPE.FAILED_DEFINITION_NO_CONSTRUCTOR,
                null,
                ex));

            }
            return null;
        }

        /**
         * Scan methods to find framework methods and test cases.
         * 
         */
        void scan()
        {
            OneTimeSetUp = AttributedMethod<OneTimeSetUpAttribute>(true);
            OneTimeTearDown = AttributedMethod<OneTimeTearDownAttribute>(true);
            SetUp = AttributedMethod<SetUpAttribute>(false);
            TearDown = AttributedMethod<TearDownAttribute>(false);


            foreach (MethodInfo m in Type.GetMethods())
            {
                if (TestCasePattern != null && !TestCasePattern.IsMatch(m.Name))
                    continue;
                if (m.GetCustomAttribute<TestCaseAttribute>() != null
                    && isSignatureValid(m, false, 0))
                {
                    TestMethod testCase = new TestMethod(m, false);
                    testCases.Add(testCase);
                }
            }

        }

        TestMethod AttributedMethod<A>(bool isStatic) where A : Attribute {
            return AttributedMethod<A>(Type, isStatic);

        }


        /**
         * Check for a method with given annotation.
         * 
         * 
         */
        TestMethod AttributedMethod<A>(Type type, bool isStatic) where A : Attribute
        {
            TestMethod result = null;


            foreach (MethodInfo m in type.GetMethods())
            {
                if (m.GetCustomAttribute<A>() != null
                       && isSignatureValid(m, isStatic, 0))
                {
                    result = new TestMethod(m, true);
                }

            }
            if (result == null && type.BaseType != null)
            {
                return AttributedMethod<A>(type.BaseType, isStatic);
            }
            return result;
        }




        bool isSignatureValid(MethodInfo method, bool IsStatic, int ParamCount)

        {
            if (IsStatic && !method.IsStatic) return false;
            if (method.GetParameters().Length != ParamCount) return false;

            return true;

        }


        /**
         * Running all test cases.
         */
        public void RunTestCases(TestReporter reporter)
        {
            TestResultEventHandler += ((o, e) => { reporter.report((TestResultEvent)e); });
            foreach (TestMethod m in TestCases)
            {
                m.TestResultEventHandler += ((o, e) => { reporter.report((TestResultEvent)e); });
            }

            foreach (TestMethod m in FrameworkTestMethods)
            {
                m.TestResultEventHandler += ((o, e) => { reporter.report((TestResultEvent)e); });
            }


            if (OneTimeSetUp != null && !OneTimeSetUp.run(null))
            {
                // if OneTimeSetUp fails mark all testcases as NOT_RUN
                foreach (TestMethod testCase in TestCases)
                {
                    TestResultEventHandler(this, new TestResultEvent(
                        RESULT_TYPE.NO_RUN_FAILED_ONE_TIME_SETUP,
                        testCase,
                        null));
                }

                return;
            }

            foreach (TestMethod testCase in TestCases)
            {
                object testInstance = CreateTestInstance();
                if (SetUp != null) SetUp.run(testInstance);
                testCase.run(testInstance);
                if (TearDown != null) TearDown.run(testInstance);
            }
            if (OneTimeTearDown != null) OneTimeTearDown.run(null);
        }
    }


    /**
     * ---------------------------------------------------------------
     *   A test method is the atomic unit of execution.
     *   A test method is isolated from any other method.
     * ----------------------------------------------------------------
     */
    public class TestMethod
    {
        public string Name
        {
            get
            {
                return Method.DeclaringType.Name + "." + Method.Name;
            }
        }
        public MethodInfo Method { get; set; }
        public bool IsFrameworkMethod { get; set; }
        public event EventHandler TestResultEventHandler;


        private static object[] EMPTY_METHOD_PARAMETERS = null;

        public TestMethod(MethodInfo m, bool framework)
        {
            if (m == null) throw new ArgumentException("TestMethod can not be null");
            Method = m;
            IsFrameworkMethod = framework;
        }

        public virtual void NotifyTestEvent(TestResultEvent args)
        {
            if (TestResultEventHandler != null) TestResultEventHandler(this, args);
        }


        /**
          * Runs itself by reflection.
          * @param target on which object the method is invoked. 
          * null implies it is static method
          * 
          * @return false if method has failed to execute.
          * true if method has not run or null.
          */
        public bool run(object target)
        {
            try
            {
                NotifyTestEvent(new TestResultEvent(RESULT_TYPE.TEST_STARTED, this, null));
                Method.Invoke(target, EMPTY_METHOD_PARAMETERS);
                NotifyTestEvent(createResultEvent());
                return true;
            }
            catch (TargetInvocationException ex)
            {
                NotifyTestEvent(createResultEvent(ex.InnerException));

            }
            catch (Exception ex)
            {
                TestReporter.printStackTrace(ex);
                NotifyTestEvent(new TestResultEvent(RESULT_TYPE.FAILED_EXECUTION, this, ex));
            }
            return false;
        }


        TestResultEvent createResultEvent()
        {
            RESULT_TYPE type = RESULT_TYPE.SUCCESS;
            Exception error = null;

            ExpectedExceptionAttribute attr = Method.GetCustomAttribute<ExpectedExceptionAttribute>();
            if ( attr != null)
            {
                type = RESULT_TYPE.FAILED_ASSERTION;
                error = new AssertException("Expected " + attr.ExpectedExceptionType
                                             + " but test method did not raise an exception");
            }
            return new TestResultEvent(type, this, error);

        }


        TestResultEvent createResultEvent(Exception ex)
        {
            RESULT_TYPE type = RESULT_TYPE.SUCCESS;
            Exception error = ex;

            ExpectedExceptionAttribute attr = Method.GetCustomAttribute<ExpectedExceptionAttribute>();
            if (attr != null)
            {
                Type ExpectedExceptionType = attr.ExpectedExceptionType;
                Type ActualExceptionType = ex.GetType();
                if (ExpectedExceptionType.IsAssignableFrom(ActualExceptionType))
                {
                    if (attr.Message == null || ex.Message.Contains(attr.Message))
                    {
                        type = RESULT_TYPE.SUCCESS;
                        error = ex;
                    } else {
                        type = RESULT_TYPE.FAILED_ASSERTION;
                        error = new AssertException("Expected exception message to contain " +
                                                    "[" + attr.Message + "] but actual message was " +
                                                    "[" + ex.Message + "]");
                    }
                }
                else {
                    type = RESULT_TYPE.FAILED_ASSERTION;
                    error = new AssertException("Expected " + ExpectedExceptionType
                                                + " but was " + ActualExceptionType);

                }
            }
            else {
                error = ex;
                type  = ex is AssertException 
                      ? RESULT_TYPE.FAILED_ASSERTION: RESULT_TYPE.FAILED_EXECUTION;
            }
            return new TestResultEvent(type, this, error);
        }

    }


    /**
     * -------------------------------------------------------------------
     * Enumeration of type of events and errors raised bythe test framework
      * -------------------------------------------------------------------
    */
    public enum RESULT_TYPE
    {
        SUCCESS,
        TEST_STARTED,
        FAILED_FIXTURE, FAILED_SETUP,
        FAILED_ASSERTION,
        FAILED_EXECUTION,
        NO_RUN_FAILED_ONE_TIME_SETUP,
        NO_RUN_FAILED_SETUP,
        FAILED_DEFINITION_NO_TESTCASE,
        FAILED_DEFINITION_MULTIPLE_SETUP_METHOD,
        FAILED_DEFINITION_NO_CONSTRUCTOR
    };

    /**
     * -------------------------------------------------------------------
     * An event raised by the test framework.
     * -------------------------------------------------------------------
    */
    public class TestResultEvent : EventArgs
    {

        public RESULT_TYPE Nature { get; private set; }
        public TestMethod Method { get; private set; }
        public Exception Error { get; private set; }

        public TestResultEvent(RESULT_TYPE f, TestMethod m, Exception e)
        {
            Nature = f;
            Method = m;
            Error = e;
        }


    }

    /**
     * -------------------------------------------------------------------
     * A test reporter is notified of various test event.
     * -------------------------------------------------------------------
     */
    public class TestReporter
    {
        List<TestResultEvent> passed = new List<TestResultEvent>();
        List<TestResultEvent> failed = new List<TestResultEvent>();
        List<TestResultEvent> errored = new List<TestResultEvent>();
        List<TestResultEvent> didNotRun = new List<TestResultEvent>();

        public void report(TestResultEvent data)
        {
            bool IsframeworkMethod = data.Method.IsFrameworkMethod;
            if (!(IsframeworkMethod 
                 || data.Nature == RESULT_TYPE.TEST_STARTED 
                  || data.Nature == RESULT_TYPE.SUCCESS))
            Console.WriteLine("" + data.Nature + " " + data.Method.Name);
            switch (data.Nature)
            {
                case RESULT_TYPE.SUCCESS:
                    if (!IsframeworkMethod) passed.Add(data);
                    break;

                case RESULT_TYPE.FAILED_ASSERTION:
                    Console.WriteLine("\t" + data.Error.Message);
                    if (!IsframeworkMethod) failed.Add(data);
                    break;

                case RESULT_TYPE.TEST_STARTED:
                    break;

                case RESULT_TYPE.FAILED_EXECUTION:
                    if (data.Error != null)
                    {
                        printStackTrace(data.Error);
                    }
                    errored.Add(data);
                    break;

                case RESULT_TYPE.FAILED_SETUP:
                case RESULT_TYPE.FAILED_DEFINITION_MULTIPLE_SETUP_METHOD:
                case RESULT_TYPE.FAILED_DEFINITION_NO_CONSTRUCTOR:
                case RESULT_TYPE.FAILED_DEFINITION_NO_TESTCASE:
                case RESULT_TYPE.NO_RUN_FAILED_SETUP:
                case RESULT_TYPE.NO_RUN_FAILED_ONE_TIME_SETUP:
                    if (!IsframeworkMethod) didNotRun.Add(data);
                    break;
                default:
                    Console.WriteLine("***WARN: Not handled " + data.Nature);
                    break;

            }

        }


        public void reportStats()
        {


            int total = passed.Count + failed.Count +
                              errored.Count + didNotRun.Count;

            Console.Write("Total:" + total);
            Console.Write(" Passed:" + passed.Count);
            if (failed.Count > 0)    Console.Write(" Failed:" + failed.Count);
            if (errored.Count > 0)   Console.Write(" Error:" + errored.Count);
            if (didNotRun.Count > 0) Console.Write(" Not Run    :" + didNotRun.Count);
            Console.WriteLine();

            printList("Passed (" + passed.Count + "/" + total + ")", passed);
            printList("Failed (" + failed.Count + "/" + total + ")", failed, true);
            printList("Error(" + errored.Count + "/" + total + ")", errored, true);
            printList("Not Run(" + didNotRun.Count + "/" + total + ")", didNotRun);

        }

        void printList(string header, List<TestResultEvent> events)
        {
            printList(header, events, false);
        }

        void printList(string header, List<TestResultEvent> events, bool error)
        {
            if (events.Count == 0) return;
            Console.WriteLine(header);
            foreach (TestResultEvent e in events)
            {
                Console.WriteLine("\t" + e.Method.Name);
                if (error)
                {
                    if (e.Error == null)
                    {
                        Console.WriteLine("\t*** No error information available");
                    }
                    else if (e.Error is AssertException)
                    {
                        Console.WriteLine("\t*** Error message:" + e.Error.Message);
                    }
                    else {
                        Console.WriteLine("\t*** Error meseage:" + e.Error.GetType()
                                          + " [" + e.Error.Message + "]");
                        if (events.Count <= 2)
                        {
                            Console.WriteLine("\t*** Error Stack:");
                            printStackTrace(e.Error);
                        }
                    }
                }
            }
        }

        public static void printStackTrace(Exception ex)
        {
            Console.WriteLine("\t" + ex.GetType() + ":" + ex.Message);
            var st = new StackTrace(ex, true);
            string indent = "\t  ";
            foreach (StackFrame sf in st.GetFrames())
            {
                MethodBase m = sf.GetMethod();
                if (m == null)
                {
                    continue;
                }
                Console.Write(indent);
                Console.Write(m.DeclaringType + "." + m.Name + "() ");
                string file = sf.GetFileName();
                if (file != null && file.Trim().Length > 0)
                {
                    Console.Write(file + ":" + sf.GetFileLineNumber());
                }
                Console.WriteLine("");
            }
            Console.WriteLine("");

        }
    }

    /**
     * -------------------------------------------------------------------
     * Exception raised when an assertion fails. Used to distinguish from 
     * when a test fails to execute or throws unhandled exception.
     * -------------------------------------------------------------------
     */
    public class AssertException : Exception
    {
        public AssertException(string msg) : base(msg) { }
    }


    /**
     * -------------------------------------------------------------------
     * Static utilty to assert validity of a test and raise AssertException 
     * -------------------------------------------------------------------
     */
    public static class Assert
    {

        public static void IsTrue(bool condition)
        {
            IsTrue(condition, "");
        }

        public static void IsTrue(bool condition, string msg)
        {
            if (!condition) throw new AssertException(msg);
        }

        public static void IsFalse(bool condition)
        {
            IsTrue(!condition);
        }

        public static void IsFalse(bool condition, string msg)
        {
            IsTrue(!condition, msg);
        }

        public static void AreEqual(object expected, object actual, string msg)
        {

            IsTrue(equals(expected, actual), msg);
        }

        public static void AreEqualIntValue(int expected, object actual)
        {
            string msg = "Expected=" + expected + "(int) actual=" + actual +
                (actual == null ? "" : "(" + actual.GetType() + ")");
            AreEqualIntValue(expected, actual, msg);
        }


        public static void AreEqualIntValue(int expected, object actual, string msg)
        {
            IsTrue(int.Parse(actual.ToString()) == expected, msg);
        }

        public static void AreEqualFloatValue(float expected, object actual, float tolerance)
        {
            string msg = "Expected=" + expected + "(float) actual=" + actual +
                (actual == null ? "" : "(" + actual.GetType() + ")");
            AreEqualFloatValue(expected, actual, tolerance, msg);
        }

        public static void AreEqualFloatValue(float expected, object actual, float tolerance,
                                              string msg) {
            
             IsTrue(Math.Abs(float.Parse(actual.ToString()) - expected) < tolerance, msg);
       }
    
        public static void AreSame(object expected, object actual, string msg)
        {
            IsTrue(Object.ReferenceEquals(expected, actual), msg);
        }

        public static void AreNotSame(object expected, object actual, string msg)
        {
            if (expected == null)
            {

                IsTrue(actual == null, msg);
            } else if (actual == null) {
                IsTrue(expected == null, msg);
            }
            else IsTrue(expected.GetHashCode() != actual.GetHashCode(), msg);
        }

        public static void AreEqual(object expected, object actual)
        {
            String msg = "Not equal. Expected=" + expected + " Actual=" + actual;
            AreEqual(expected, actual, msg);
        }

        public static void AreSame(object expected, object actual)
        {
            String msg = "Not same. Expected=" + expected + " to be same as Actual=" + actual;
            AreSame(expected, actual, msg);
        }

        public static void AreNotSame(object expected, object actual)
        {
            String msg = "Same. Expected=" + expected + "@" + expected.GetHashCode() + " to be not be same as Actual=" + actual + "@" + actual.GetHashCode();
            AreNotSame(expected, actual, msg);
        }

        public static void IsNull(object obj)
        {
            if (obj != null)
            {
                throw new AssertException("" + obj.GetType() + " was expected to be null. but was " + obj);
            }
        }

        public static void IsNotNull(object obj)
        {
            IsTrue(obj != null, "Expected to be not null but is null");
        }

        public static void IsNull(object obj, string msg)
        {
            IsTrue(obj == null, msg);
        }

        public static void IsNotNull(object obj, string msg)
        {
            IsTrue(obj != null, msg);
        }

        public static void Fail()
        {
            Fail("Expected to fail");
        }

        public static void Fail(string msg)
        {
            IsTrue(true, msg);
        }

        public static bool equals(object expected, object actual)
        {
            if (expected == null) return actual == null;

            return expected.Equals(actual);
        }
    }


}
